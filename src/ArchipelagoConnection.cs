using System;
using System.Linq;
using UnityEngine.Events;
using Archipelago.MultiClient.Net;
using Archipelago.MultiClient.Net.Enums;
using Archipelago.MultiClient.Net.Models;
using Archipelago.MultiClient.Net.Helpers;


internal static class ArchipelagoConnection
{
    private static ArchipelagoSession session;
    private static bool connected;

    public static void Connect(string host, int port, string slot, string password)
    {
        // Dispose previous session if any
        session?.Socket?.DisconnectAsync();
        session = null;
        connected = false;

        // Create & login
        session = ArchipelagoSessionFactory.CreateSession(host, port);
        var result = session.TryConnectAndLogin("Valheim", slot,
            ItemsHandlingFlags.AllItems, password: password);

        if (!result.Successful)
        {
            // Throw the first error up; UI handles it and shows the message
            var failure = (LoginFailure)result;
            throw new Exception(string.Join("; ", failure.Errors ?? Array.Empty<string>()));
        }


        if (result.Successful)
        {
           var loginSuccess = (LoginSuccessful)result;
           if (loginSuccess.SlotData.TryGetValue("goal", out var goal_string))
           {
             ValheimRandomizer.Goal = Convert.ToString(goal_string);
           }
        }

        // A new session can have a different item stream.  Do not let the
        // previous connection suppress its chat announcements.
        gottenItems.Clear();

        // Hook item reception
        session.Items.ItemReceived += OnApItemReceived;

        connected = true;
    }

    public static void SendLocation(string locationName)
    {
        if (!ValheimRandomizer.randomized.Value) return;
        if (!WorldLoaded()) return;
        if (string.IsNullOrWhiteSpace(locationName)) return;

        ValheimRandomizer.Log.LogInfo("Queue location " + locationName);

        if (HasGlobal($"ap_sent:{locationName}")) return;
        if (ValheimRandomizer.researchToArchipelago.TryGetValue(locationName, out var displayName)
            && HasGlobal($"ap_sent:{displayName}")) return;

        SetGlobal($"ap_pending:{locationName}");
        TryFlushPendingLocations();
    }

    private static void OnApItemReceived(ReceivedItemsHelper helper)
    {
        if (!WorldLoaded()) return;
        while (true)
        {
            var item = helper.DequeueItem();
            if (item == null) break;
            ProcessReceivedItem(item, showLocalMessage: true);
        }
    }

    // A research can only be unlocked once, so one chat message per item name is
    // enough even if the server replays the item after reconnecting.  This also
    // prevents the initial AllItemsReceived pass and ItemReceived event from
    // announcing the same item twice.
    static readonly System.Collections.Generic.HashSet<string> gottenItems =
        new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);

    private static void SendChatMessage(string message)
    {
        if (!connected || session == null || string.IsNullOrWhiteSpace(message)) return;

        try
        {
            // Say sends the message to the Archipelago room chat, where it is
            // visible to all players.  Keep this separate from MessageHud: the
            // latter is local to Valheim and is easy to miss while playing.
            session.Say($"[Valheim] {message}");
        }
        catch (Exception ex)
        {
            // Chat is only an informational add-on; never let a chat failure
            // interrupt location or item processing.
            ValheimRandomizer.Log.LogWarning($"Unable to send AP chat message: {ex.Message}");
        }
    }

    private static void AddToGameChat(string message)
    {
        if (string.IsNullOrWhiteSpace(message) || Chat.instance == null) return;

        try
        {
            // AddString writes to the local Valheim chat history. It does not
            // broadcast a second network message to the room.
            Chat.instance.AddString("Archipelago", message, Talker.Type.Normal);
        }
        catch (Exception ex)
        {
            ValheimRandomizer.Log.LogWarning($"Unable to add AP message to game chat: {ex.Message}");
        }
    }

    private static void ProcessReceivedItem(ItemInfo item, bool showLocalMessage)
    {
        var name = item.ItemName;
        if (string.IsNullOrEmpty(name)) return;

        if (!gottenItems.Add(name)) return;

        var sender = session.Players.GetPlayerAliasAndName(item.Player);
        if (string.IsNullOrEmpty(sender)) sender = $"player {item.Player}";

        // Unlike the local MessageHud notification, this also tells the other
        // players which reward arrived and who sent it.
        SendChatMessage($"Received item '{name}' from {sender}.");

        if (ValheimRandomizer.archipelagoToResearch.TryGetValue(name, out var researchId))
        {
            if (showLocalMessage)
            {
                var receivedMessage = $"Received {name} from {sender}!";
                MessageHud.instance.ShowMessage(MessageHud.MessageType.Center, receivedMessage);
                AddToGameChat(receivedMessage);
            }
            ValheimRandomizer.DoUnlockResearch(researchId);
        }
        else
        {
            ValheimRandomizer.Log.LogWarning(
                $"Received AP item '{name}' but no research mapping was found.");
        }
    }

    static void TryGetAllItems()
    {
        if (!WorldLoaded()) return;

        foreach (var item in session.Items.AllItemsReceived)
        {
            if (item == null) continue;
            ProcessReceivedItem(item, showLocalMessage: false);
        }
    }

    public static void TryFlushPendingLocations()
    {
        if (!connected || session == null) return;
        if (!WorldLoaded()) return;
        TryGetAllItems();

        // Work on a snapshot because successful checks remove their pending key.
        foreach (var key in ZoneSystem.instance.GetGlobalKeys().ToList())
        {
            if (!key.StartsWith("ap_pending:", StringComparison.Ordinal)) continue;

            var researchId = key.Substring("ap_pending:".Length);
            if (!ValheimRandomizer.researchToArchipelago.TryGetValue(researchId, out var locationName)
                || string.IsNullOrWhiteSpace(locationName))
            {
                ValheimRandomizer.Log.LogWarning(
                    $"No AP location mapping was found for research '{researchId}'.");
                continue;
            }

            // Older versions used the display name for this global key.  Accept
            // both formats so an existing world does not submit the check again.
            if (HasGlobal($"ap_sent:{researchId}"))
            {
                RemoveGlobal(key);
                continue;
            }

            if (HasGlobal($"ap_sent:{locationName}"))
            {
                RemoveGlobal(key);
                continue;
            }

            try
            {
                foreach (string str in ValheimRandomizer.archipelagoToResearch.Keys)
                {
                    if (string.Equals(str, locationName, StringComparison.OrdinalIgnoreCase))
                    {
                        locationName = str;
                        break;
                    }
                }

                ValheimRandomizer.Log.LogInfo("Unlock location " + locationName);
                var id = session.Locations.GetLocationIdFromName("Valheim", locationName);
                session.Locations.CompleteLocationChecks(id);

                // Report the completed check both locally and to the AP room
                // chat after the location was accepted by the client helper.
                var sentMessage = $"Sent check '{locationName}'.";
                SendChatMessage(sentMessage);
                AddToGameChat(sentMessage);

                // Store the stable research ID.  It avoids duplicate checks and
                // remains valid if only the display name is changed later.
                SetGlobal($"ap_sent:{researchId}");
                RemoveGlobal(key);
            }
            catch (Exception ex)
            {
                ValheimRandomizer.Log.LogWarning(
                    $"AP location lookup failed for '{locationName}'. " +
                    $"Check the exact name in your AP world. Error: {ex.Message}");
                // Keep pending; retry on the next tick/reconnect.
            }
        }
    }

    private static bool WorldLoaded()
        => ZoneSystem.instance != null && Player.m_localPlayer != null; // simple & reliable

    private static bool HasGlobal(string k)
        => ZoneSystem.instance != null && ZoneSystem.instance.GetGlobalKey(k);

    private static void SetGlobal(string k)
    {
        if (ZoneSystem.instance == null) return;
        ZoneSystem.instance.SetGlobalKey(k);
    }

    private static void RemoveGlobal(string k)
    {
        if (ZoneSystem.instance == null) return;
        ZoneSystem.instance.RemoveGlobalKey(k);
    }

    public static void CompleteGame()
    {
        session.SetGoalAchieved();
    }
}

using System;
using System.Linq;
using UnityEngine.Events;
using Archipelago.MultiClient.Net;
using Archipelago.MultiClient.Net.Enums;
using Archipelago.MultiClient.Net.Models;
using Archipelago.MultiClient.Net.Helpers;
using Archipelago.MultiClient.Net.MessageLog.Messages;
using Archipelago.MultiClient.Net.Packets;


internal static class ArchipelagoConnection
{
    private static ArchipelagoSession session;
    private static bool connected;
    private static bool receivedItemsInitialized;

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
        receivedItemsInitialized = false;
        MainThreadDispatcher.Clear();

        // Subscribe after login so the initial room synchronization does not
        // echo old item sends into the local game chat.
        session.MessageLog.OnMessageReceived += OnApMessageReceived;
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

    private static void OnApMessageReceived(LogMessage message)
    {
        if (session == null) return;

        var chatMessage = message as ChatLogMessage;
        if (chatMessage != null)
        {
            // Messages sent by this client are already visible in the local
            // Valheim chat. Do not echo those a second time when AP sends them
            // back through room chat.
            if (chatMessage.IsActivePlayer
                && chatMessage.Message != null
                && chatMessage.Message.StartsWith(ApChatPrefix, StringComparison.Ordinal))
            {
                return;
            }

            var sender = session.Players.GetPlayerAliasAndName(chatMessage.Player.Slot);
            if (string.IsNullOrWhiteSpace(sender)) sender = "Archipelago";
            AddToGameChat(sender, chatMessage.Message);
            return;
        }

        var serverChatMessage = message as ServerChatLogMessage;
        if (serverChatMessage != null)
        {
            AddToGameChat("Archipelago", serverChatMessage.Message);
            return;
        }

        var itemSendMessage = message as ItemSendLogMessage;
        if (itemSendMessage == null || !itemSendMessage.IsSenderTheActivePlayer)
        {
            return;
        }

        try
        {
            var itemName = itemSendMessage.Item.ItemName;
            var checkName = itemSendMessage.Item.LocationName;
            var receiver = session.Players.GetPlayerAliasAndName(itemSendMessage.Receiver.Slot);
            if (string.IsNullOrWhiteSpace(itemName)) itemName = "unknown item";
            if (string.IsNullOrWhiteSpace(checkName)) checkName = "unknown check";
            if (string.IsNullOrWhiteSpace(receiver)) receiver = $"player {itemSendMessage.Receiver.Slot}";

            var sentItemMessage = $"Sent item \"{itemName}\" ({checkName}) to {receiver}";
            AddToGameChat(sentItemMessage);
            ShowCenterMessage(sentItemMessage);
        }
        catch (Exception ex)
        {
            ValheimRandomizer.Log.LogWarning($"Unable to show sent AP item in game chat: {ex.Message}");
        }
    }

    private static void OnApItemReceived(ReceivedItemsHelper helper)
    {
        // Raised on the Archipelago socket thread; everything below touches
        // Unity state, so hand it over to the main thread.
        MainThreadDispatcher.Enqueue(() =>
        {
            if (!WorldLoaded()) return;
            while (true)
            {
                var item = helper.DequeueItem();
                if (item == null) break;
                // The first AllItemsReceived pass contains the complete historical
                // inventory. Only items arriving after that pass are announced.
                ProcessReceivedItem(
                    item,
                    showLocalMessage: receivedItemsInitialized);
            }
        });
    }

    // A research can only be unlocked once, so one chat message per item name is
    // enough even if the server replays the item after reconnecting.  This also
    // prevents the initial AllItemsReceived pass and ItemReceived event from
    // announcing the same item twice.
    static readonly System.Collections.Generic.HashSet<string> gottenItems =
        new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);

    private static void ShowCenterMessage(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        MainThreadDispatcher.Enqueue(() => ShowCenterMessageOnMainThread(message));
    }

    private static void ShowCenterMessageOnMainThread(string message)
    {
        if (MessageHud.instance == null) return;

        try
        {
            // Use Valheim's standard center-message queue. Its normal display
            // time is close to three seconds and it keeps messages ordered.
            MessageHud.instance.ShowMessage(MessageHud.MessageType.Center, message);
        }
        catch (Exception ex)
        {
            ValheimRandomizer.Log.LogWarning($"Unable to show AP message on screen: {ex.Message}");
        }
    }

    /// <summary>
    /// Local chat prefix used for messages this client forwards into the
    /// Archipelago room, so the echo coming back can be filtered out.
    /// </summary>
    public const string ApChatPrefix = "[Valheim] ";

    /// <summary>
    /// Sends a line written by the local player into the Archipelago room chat,
    /// where other games (Stardew Valley, ...) can pick it up.
    /// </summary>
    public static void SendChatToArchipelago(string message)
    {
        if (!connected || session?.Socket == null) return;
        if (string.IsNullOrWhiteSpace(message)) return;

        try
        {
            session.Socket.SendPacketAsync(new SayPacket { Text = ApChatPrefix + message });
        }
        catch (Exception ex)
        {
            ValheimRandomizer.Log.LogWarning($"Unable to send chat to Archipelago: {ex.Message}");
        }
    }

    private static void AddToGameChat(string message)
        => AddToGameChat("Archipelago", message);

    private static void AddToGameChat(string sender, string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        if (string.IsNullOrWhiteSpace(sender)) sender = "Archipelago";

        // Archipelago raises its events on the socket thread.  Touching Unity
        // objects from there throws, so the actual chat write is deferred to
        // the next Update tick on the main thread.
        MainThreadDispatcher.Enqueue(() => AddToGameChatOnMainThread(sender, message));
    }

    private static void AddToGameChatOnMainThread(string sender, string message)
    {
        if (Chat.instance == null) return;

        try
        {
            // AddString writes to the local Valheim chat history. It does not
            // broadcast a second network message to the room.
            Chat.instance.AddString(sender, message, Talker.Type.Normal);
        }
        catch (Exception ex)
        {
            ValheimRandomizer.Log.LogWarning($"Unable to add AP message to game chat: {ex.Message}");
        }
    }

    private static void ProcessReceivedItem(
        ItemInfo item,
        bool showLocalMessage)
    {
        var name = item.ItemName;
        if (string.IsNullOrEmpty(name)) return;

        if (!gottenItems.Add(name)) return;

        var sender = session.Players.GetPlayerAliasAndName(item.Player);
        if (string.IsNullOrEmpty(sender)) sender = $"player {item.Player}";

        // Historical items are still applied below, but are not announced.
        // This prevents reconnecting from filling the room and game chats with
        // every item ever received by the slot.
        if (ValheimRandomizer.archipelagoToResearch.TryGetValue(name, out var researchId))
        {
            if (showLocalMessage)
            {
                var receivedMessage = $"Received {name} from {sender}!";
                ShowCenterMessage(receivedMessage);
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
            ProcessReceivedItem(
                item,
                showLocalMessage: false);
        }

        // From this point on ItemReceived represents items arriving during the
        // current connection, rather than the historical sync.
        receivedItemsInitialized = true;
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

                // Report the completed check in the local Valheim chat after
                // the location was accepted by the client helper.
                var sentMessage = $"Sent check '{locationName}'.";
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

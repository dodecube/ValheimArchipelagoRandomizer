using System;
using System.Linq;
using UnityEngine.Events;
using Archipelago.MultiClient.Net;
using Archipelago.MultiClient.Net.Enums;
using Archipelago.MultiClient.Net.Models;
using Archipelago.MultiClient.Net.Helpers;
using Archipelago.MultiClient.Net.MessageLog.Messages;


internal static class ArchipelagoConnection
{
    private static ArchipelagoSession session;
    private static bool connected;
    private static bool receivedItemsInitialized;
    private static bool completedLocationsSynchronized;

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
        unmappedItemsWarned.Clear();
        receivedItemsInitialized = false;
        completedLocationsSynchronized = false;

        // Subscribe after login so the initial room synchronization does not
        // echo old item sends into the local game chat.
        session.MessageLog.OnMessageReceived += OnApMessageReceived;
        session.Items.ItemReceived += OnApItemReceived;

        connected = true;
    }

    public static void SendLocation(string researchId)
    {
        if (!ValheimRandomizer.randomized.Value) return;
        if (!WorldLoaded()) return;
        if (string.IsNullOrWhiteSpace(researchId)) return;

        ValheimRandomizer.Log.LogInfo("Queue location " + researchId);

        // These flags must be local to the AP slot.  ZoneSystem global keys are
        // shared by everyone in a co-op world and previously let one client
        // flush another client's pending checks.
        if (ValheimRandomizer.IsLocationSent(researchId)) return;

        ValheimRandomizer.QueueLocation(researchId);
        TryFlushPendingLocations();
    }

    private static void OnApMessageReceived(LogMessage message)
    {
        if (session == null) return;

        var chatMessage = message as ChatLogMessage;
        if (chatMessage != null)
        {
            const string localPrefix = "[Valheim] ";

            // Messages sent by this mod are already added locally when the
            // event happens. Do not echo those messages a second time when AP
            // sends them back through room chat.
            if (chatMessage.IsActivePlayer
                && chatMessage.Message != null
                && chatMessage.Message.StartsWith(localPrefix, StringComparison.Ordinal))
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
    }

    // A research can only be unlocked once, so one chat message per item name is
    // enough even if the server replays the item after reconnecting.  This also
    // prevents the initial AllItemsReceived pass and ItemReceived event from
    // announcing the same item twice.
    static readonly System.Collections.Generic.HashSet<string> gottenItems =
        new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);

    // Keep retrying unmapped items because definitions may still be loading,
    // but avoid a warning every two seconds for a permanently mismatched TSV.
    static readonly System.Collections.Generic.HashSet<string> unmappedItemsWarned =
        new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);

    private static void ShowCenterMessage(string message)
    {
        if (string.IsNullOrWhiteSpace(message) || MessageHud.instance == null) return;

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

    private static void AddToGameChat(string message)
        => AddToGameChat("Archipelago", message);

    private static void AddToGameChat(string sender, string message)
    {
        if (string.IsNullOrWhiteSpace(message) || Chat.instance == null) return;
        if (string.IsNullOrWhiteSpace(sender)) sender = "Archipelago";

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

        // Definitions are registered a few seconds after the game loads.  Do
        // not mark an item as processed until its mapping exists, otherwise an
        // initial AP sync can permanently discard a valid received technology.
        if (!ValheimRandomizer.archipelagoToResearch.TryGetValue(name, out var researchId))
        {
            if (unmappedItemsWarned.Add(name))
            {
                ValheimRandomizer.Log.LogWarning(
                    $"Received AP item '{name}' but no research mapping was found.");
            }
            return;
        }

        unmappedItemsWarned.Remove(name);
        if (!gottenItems.Add(name)) return;

        var sender = session.Players.GetPlayerAliasAndName(item.Player);
        if (string.IsNullOrEmpty(sender)) sender = $"player {item.Player}";

        // Historical items are still applied below, but are not announced.
        // This prevents reconnecting from filling the room and game chats with
        // every item ever received by the slot.
        if (showLocalMessage)
        {
            var receivedMessage = $"Received {name} from {sender}!";
            ShowCenterMessage(receivedMessage);
            AddToGameChat(receivedMessage);
        }
        ValheimRandomizer.DoUnlockResearch(researchId);
    }

    static void TryGetAllItems()
    {
        if (!WorldLoaded()) return;

        // AddResearchRecipes and AddTrophyResearches populate this map after
        // prefab registration.  Wait instead of consuming the initial item
        // stream before these definitions are available.
        if (ValheimRandomizer.archipelagoToResearch.Count == 0) return;

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
        if (!completedLocationsSynchronized && ValheimRandomizer.researchToArchipelago.Count > 0)
        {
            SyncCompletedLocations();
            completedLocationsSynchronized = true;
        }

        // Pending/sent state is stored with the local player rather than in
        // ZoneSystem.  A player may therefore only submit checks for their own
        // configured Archipelago slot.
        foreach (var researchId in ValheimRandomizer.GetPendingLocations())
        {
            if (!ValheimRandomizer.researchToArchipelago.TryGetValue(researchId, out var locationName)
                || string.IsNullOrWhiteSpace(locationName))
            {
                ValheimRandomizer.Log.LogWarning(
                    $"No AP location mapping was found for research '{researchId}'.");
                continue;
            }

            if (ValheimRandomizer.IsLocationSent(researchId))
            {
                ValheimRandomizer.RemovePendingLocation(researchId);
                continue;
            }

            try
            {
                ValheimRandomizer.Log.LogInfo("Unlock location " + locationName);
                var id = session.Locations.GetLocationIdFromName("Valheim", locationName);

                // The server can already know the check after a reconnect or a
                // successful earlier request.  Mark it locally without sending
                // a duplicate in that case.
                if (session.Locations.AllLocationsChecked.Contains(id))
                {
                    MarkLocationCompleted(researchId);
                    continue;
                }

                session.Locations.CompleteLocationChecks(id);

                // CompleteLocationChecks throws if it cannot queue the packet.
                // Once queued, keep the stable ID in this character's state so
                // reconnecting this AP slot does not submit the check again.
                ValheimRandomizer.SetLocationSent(researchId);
                ValheimRandomizer.RemovePendingLocation(researchId);

                var sentMessage = $"Sent check '{locationName}'.";
                AddToGameChat(sentMessage);
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

    private static void SyncCompletedLocations()
    {
        // Version 0.2.5 kept completed research in world-global keys.  Ask AP
        // for the connected slot's authoritative checked-location list instead.
        // This restores only this player's completed research after upgrading,
        // without copying another co-op player's global progress.
        foreach (var entry in ValheimRandomizer.researchToArchipelago)
        {
            try
            {
                long locationId = session.Locations.GetLocationIdFromName("Valheim", entry.Value);
                if (!session.Locations.AllLocationsChecked.Contains(locationId)) continue;

                MarkLocationCompleted(entry.Key);
            }
            catch (Exception ex)
            {
                ValheimRandomizer.Log.LogWarning(
                    $"AP location lookup failed while syncing '{entry.Value}': {ex.Message}");
            }
        }
    }

    private static void MarkLocationCompleted(string researchId)
    {
        ValheimRandomizer.SetResearchCrafted(researchId);
        ValheimRandomizer.SetLocationSent(researchId);
        ValheimRandomizer.RemovePendingLocation(researchId);
    }

    private static bool WorldLoaded()
        => ZoneSystem.instance != null && Player.m_localPlayer != null; // simple & reliable

    public static void CompleteGame()
    {
        session.SetGoalAchieved();
    }
}

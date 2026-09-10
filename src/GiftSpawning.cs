using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Archipelago.MultiClient.Net.Models;
using UnityEngine;

/// <summary>
/// Handles gift & prank AP items (see apworld/valheim/Items.py gift_data_table).
/// Gifts spawn resource drops next to the player, pranks spawn hostile mobs.
/// Item names here MUST match Items.py exactly.
/// Unlike research unlocks, gifts are NOT idempotent, so received items are
/// tracked by server index in a file next to the DLL (see received_*.txt).
/// </summary>
public static class GiftSpawning
{
    // AP item name -> (Valheim item prefab, amount). Amounts are capped by max stack size.
    static readonly Dictionary<string, (string prefab, int amount)> ItemGifts =
        new Dictionary<string, (string prefab, int amount)>(StringComparer.OrdinalIgnoreCase)
        {
            { "Gift: Wood Bundle", ("Wood", 50) },
            { "Gift: Stone Cache", ("Stone", 50) },
            { "Gift: Coal Stash", ("Coal", 30) },
            { "Gift: Honey Pot", ("Honey", 10) },
            { "Gift: Coin Purse", ("Coins", 150) },
            { "Gift: Cooked Feast", ("CookedMeat", 5) },
        };

    // AP item name -> (Valheim character prefab, count). Spawned hostile around the player.
    static readonly Dictionary<string, (string prefab, int count)> MobPranks =
        new Dictionary<string, (string prefab, int count)>(StringComparer.OrdinalIgnoreCase)
        {
            { "Prank: Greydwarf Ambush", ("Greydwarf", 3) },
            { "Prank: Hungry Wolves", ("Wolf", 2) },
            { "Prank: Angry Troll", ("Troll", 1) },
            { "Prank: Deathsquito Swarm", ("Deathsquito", 3) },
        };

    public static bool IsGift(string name)
        => !string.IsNullOrEmpty(name) && (ItemGifts.ContainsKey(name) || MobPranks.ContainsKey(name));

    /// <summary>
    /// Scan newly received items (by persistent server index) and spawn gifts.
    /// Must be called from the main thread (it spawns Unity objects).
    /// </summary>
    public static void ProcessIncoming(IReadOnlyList<ItemInfo> all, string slotName)
    {
        if (all == null || ZNetScene.instance == null) return; // not in world yet, retry next tick

        int saved = LoadIndex(slotName);
        if (all.Count < saved)
        {
            // Server counter restarted: same slot name reused for a new multiworld.
            ValheimRandomizer.Log?.LogInfo($"AP gift index reset ({saved} -> 0, looks like a new seed on slot '{slotName}').");
            saved = 0;
        }

        for (int i = saved; i < all.Count; i++)
        {
            var name = all[i].ItemName;
            if (!IsGift(name)) continue;
            try
            {
                SpawnGift(name);
            }
            catch (Exception ex)
            {
                ValheimRandomizer.Log?.LogWarning($"Gift '{name}' failed to spawn: {ex.Message}");
            }
        }

        if (all.Count > saved) SaveIndex(slotName, all.Count);
    }

    static void SpawnGift(string name)
    {
        var player = Player.m_localPlayer;
        if (player == null) throw new InvalidOperationException("no local player");
        Vector3 feet = player.transform.position;

        if (ItemGifts.TryGetValue(name, out var gift))
        {
            var prefab = ZNetScene.instance.GetPrefab(gift.prefab);
            if (prefab == null) throw new InvalidOperationException($"unknown item prefab '{gift.prefab}'");
            Vector3 pos = feet + player.transform.forward * 1.5f + Vector3.up * 0.5f;
            var go = UnityEngine.Object.Instantiate(prefab, pos, Quaternion.identity);
            var drop = go.GetComponent<ItemDrop>();
            if (drop?.m_itemData != null && drop.m_itemData.m_shared != null)
                drop.m_itemData.m_stack = Math.Min(gift.amount, drop.m_itemData.m_shared.m_maxStackSize);
            ValheimRandomizer.Log?.LogInfo($"Spawned gift '{name}' ({gift.prefab} x{gift.amount}).");
        }
        else if (MobPranks.TryGetValue(name, out var prank))
        {
            var prefab = ZNetScene.instance.GetPrefab(prank.prefab);
            if (prefab == null) throw new InvalidOperationException($"unknown character prefab '{prank.prefab}'");
            for (int i = 0; i < prank.count; i++)
            {
                float angle = (Mathf.PI * 2f * i) / prank.count;
                Vector3 pos = feet + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * 6f + Vector3.up * 2f;
                UnityEngine.Object.Instantiate(prefab, pos, Quaternion.identity);
            }
            ValheimRandomizer.Log?.LogInfo($"Spawned prank '{name}' ({prank.prefab} x{prank.count}).");
        }

        if (MessageHud.instance != null)
            MessageHud.instance.ShowMessage(MessageHud.MessageType.Center, $"Received {name}!");
    }

    // ---------- persistent received-index (survives relogs) ----------

    static string IndexPath(string slotName)
    {
        var safe = new string(slotName.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());
        return Path.Combine(ValheimRandomizer.PluginFolder, $"received_{safe}.txt");
    }

    static int LoadIndex(string slotName)
    {
        try
        {
            string path = IndexPath(slotName);
            if (File.Exists(path) && int.TryParse(File.ReadAllText(path).Trim(), out int n) && n >= 0)
                return n;
        }
        catch (Exception ex)
        {
            ValheimRandomizer.Log?.LogWarning($"Could not load gift index: {ex.Message}");
        }
        return 0;
    }

    static void SaveIndex(string slotName, int index)
    {
        try { File.WriteAllText(IndexPath(slotName), index.ToString()); }
        catch (Exception ex)
        {
            ValheimRandomizer.Log?.LogWarning($"Could not save gift index: {ex.Message}");
        }
    }

    /// <summary>Manual reset for reused slot names (console: apgift_reset).</summary>
    public static void ResetIndex(string slotName)
    {
        try
        {
            string path = IndexPath(slotName);
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex)
        {
            ValheimRandomizer.Log?.LogWarning($"Could not reset gift index: {ex.Message}");
        }
    }
}

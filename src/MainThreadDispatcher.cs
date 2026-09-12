using System;
using System.Collections.Generic;

/// <summary>
/// Archipelago callbacks (MessageLog / ItemReceived) are raised on the socket's
/// background thread.  Unity API calls such as Chat.AddString or
/// MessageHud.ShowMessage throw when they are not made on the main thread, so
/// those callbacks silently died before the message ever reached the screen.
/// Work is queued here and drained from ValheimRandomizer.Update instead.
/// </summary>
internal static class MainThreadDispatcher
{
    private static readonly Queue<Action> pending = new Queue<Action>();

    public static void Enqueue(Action action)
    {
        if (action == null) return;
        lock (pending)
        {
            pending.Enqueue(action);
        }
    }

    public static void Drain()
    {
        // Only the items queued before this tick are processed. An action may
        // re-queue itself (e.g. chat is not ready yet) without spinning here.
        int budget;
        lock (pending)
        {
            budget = pending.Count;
        }

        for (int i = 0; i < budget; i++)
        {
            Action action;
            lock (pending)
            {
                if (pending.Count == 0) return;
                action = pending.Dequeue();
            }

            try
            {
                action();
            }
            catch (Exception ex)
            {
                ValheimRandomizer.Log?.LogWarning($"Queued Archipelago action failed: {ex.Message}");
            }
        }
    }

    public static void Clear()
    {
        lock (pending)
        {
            pending.Clear();
        }
    }
}

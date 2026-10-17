// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.IO;
using System.Threading;
using HollowSols.Foundation;

internal static class DeferredReadChecks
{
    internal static void Run()
    {
        var connection = Guid.NewGuid();
        var reader = new DeferredRead<string>();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        reader.Begin(connection, () => { entered.Set(); if (!release.Wait(5000)) throw new Exception("Worker fixture timed out"); return "old"; });
        Require(!reader.Poll(connection, out _), "Starting a read waited for its result");
        Require(entered.Wait(2000), "Read did not start on its worker");
        try {
            // Exercise the actual control implementation for longer than its
            // silence deadline while the production read worker remains blocked.
            var host = new MenuBridgeController(GameSide.HollowKnight);
            var peer = new MenuBridgeController(GameSide.NineSols);
            host.Activate(0); peer.Activate(0); Pump(host, peer, 0);
            for (int step = 1; step <= 40; step++) {
                double now = step * .5;
                host.Heartbeat(); peer.Heartbeat(); Pump(host, peer, now);
                host.Tick(now); peer.Tick(now);
                Require(!reader.Poll(connection, out _), "Blocked read unexpectedly completed");
                Require(host.Lifecycle.State == BridgeState.SaveSelection && peer.Lifecycle.State == BridgeState.Connected,
                    "File reading starved a healthy control connection");
            }
            reader.Begin(connection, () => throw new Exception("Superseded queued work ran"));
            reader.Begin(connection, () => "latest");
        } finally { release.Set(); }
        Require(Await(reader, connection) == "latest", "Retired result replaced the latest request");
        Require(!reader.Busy && !reader.Poll(connection, out _), "Read result was delivered twice");

        using var secondEntered = new ManualResetEventSlim();
        using var secondRelease = new ManualResetEventSlim();
        reader.Begin(connection, () => { secondEntered.Set(); if (!secondRelease.Wait(5000)) throw new Exception("Worker fixture timed out"); throw new IOException("Retired failure"); });
        reader.Poll(connection, out _); Require(secondEntered.Wait(2000), "Cancellation fixture did not start");
        try {
            reader.Cancel(); connection = Guid.NewGuid();
            reader.Begin(connection, () => "reconnected");
            Require(!reader.Poll(connection, out _), "A replacement overlapped the retired worker");
        } finally { secondRelease.Set(); }
        Require(Await(reader, connection) == "reconnected", "Retired error failed a later connection");

        reader.Begin(connection, () => "cancelled"); reader.Cancel();
        Require(!reader.Poll(connection, out _) && !reader.Busy, "Cancelled queued work started");
        reader.Begin(connection, () => "foreign");
        Require(!reader.Poll(Guid.NewGuid(), out _) && !reader.Busy, "Foreign connection adopted or started work");
        reader.Begin(connection, () => throw new IOException("Current read failure"));
        bool failed = false;
        try { Await(reader, connection); } catch (IOException) { failed = true; }
        Require(failed && !reader.Busy, "Current failure was hidden or left the reader busy");
        reader.Begin(connection, () => "retry");
        Require(Await(reader, connection) == "retry", "Reader could not recover after failure");
        Console.WriteLine("PASS: bounded deferred reads, 20 seconds of live control heartbeats, supersession, cancellation, reconnect and failures");
    }
    private static string Await(DeferredRead<string> reader, Guid connection)
    {
        string? result = null;
        Require(SpinWait.SpinUntil(() => reader.Poll(connection, out result), 5000), "Read result did not arrive");
        return result!;
    }
    private static void Pump(MenuBridgeController host, MenuBridgeController peer, double now)
    {
        for (int i = 0; i < 10; i++) {
            bool delivered = false;
            while (host.TryTakeOutgoing(out var message)) { peer.Receive(message!, now); delivered = true; }
            while (peer.TryTakeOutgoing(out var message)) { host.Receive(message!, now); delivered = true; }
            if (!delivered) return;
        }
        throw new Exception("Control fixture failed to settle");
    }
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
}

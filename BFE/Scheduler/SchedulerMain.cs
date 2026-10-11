using ECommons.ChatMethods;
using ECommons.DalamudServices;
using ECommons.Logging;
using FFXIVClientStructs.FFXIV.Component.GUI;
using BFE.Scheduler.Handlers;
using BFE.Scheduler.Tasks;
using BFE.Ui.MainWindow;
using System.Numerics;

namespace BFE.Scheduler
{
    internal static unsafe class SchedulerMain
    {
        public static bool HadAutoChestOn = false;
        public static bool HadAutoInteractOn = false;
        public static bool RunTurnin = false; // Used for Turnin Toggle
        public static bool BunniesRun = false; // Used for N-Raid Toggle
        public static bool hasEnqueuedDutyFinder = false; // used for enque throtle flag
        public static string BunniesTask = "idle";
        private static uint PreviousArea = 0;
        private static int ZoneSelected = 0;

        // Pyros is the only zone with an implemented bunny route
        internal const sbyte PyrosZoneIndex = 1;

        internal static bool AreWeTicking;
        internal static bool DoWeTick
        {
            get => AreWeTicking;
            private set => AreWeTicking = value;
        }

        internal static bool EnablePlugin()
        {
            if (C.zoneSelected != PyrosZoneIndex)
            {
                C.zoneSelected = PyrosZoneIndex;
                C.Save();
            }
            if (P.pandora.GetFeatureEnabled("Automatically Open Chests"))
            {
                HadAutoChestOn = true;
                P.pandora.SetFeatureEnabled("Automatically Open Chests", false);
            }
            if (P.pandora.GetFeatureEnabled("Auto-interact with Objects in Instances"))
            {
                HadAutoInteractOn = true;
                P.pandora.SetFeatureEnabled("Auto-interact with Objects in Instances", false);
            }
            /*if (PluginInstalled("RotationSolver"))
            {
                RunCommand("rsr settings AutoOpenChest false");
            }*/
            StartBunnies.IsRunning = true;
            BunniesRun = true;
            DoWeTick = true;
            return true;
        }

        internal static bool DisablePlugin()
        {
            StartBunnies.IsRunning = false;
            DoWeTick = false;
            BunniesRun = false;
            BunniesTask = "idle";
            UpdateCurrentTask("idle");

            // Each step is isolated so a missing plugin or failed IPC call cannot skip the remaining cleanup
            RunCleanupStep("abort tasks", () => P.taskManager.Abort());
            RunCleanupStep("stop navmesh", () => P.navmesh.Stop());
            RunCleanupStep("disable rotation AI", ToggleRotationAIOff);
            RunCleanupStep("reset stopwatch", () =>
            {
                P.stopwatch.Restart();
                P.stopwatch.Stop();
            });

            // Restore the Pandora features that EnablePlugin turned off
            if (HadAutoChestOn)
                RunCleanupStep("restore Pandora auto chests", () => P.pandora.SetFeatureEnabled("Automatically Open Chests", true));
            if (HadAutoInteractOn)
                RunCleanupStep("restore Pandora auto interact", () => P.pandora.SetFeatureEnabled("Auto-interact with Objects in Instances", true));
            HadAutoChestOn = false;
            HadAutoInteractOn = false;
            return true;
        }

        private static void RunCleanupStep(string name, Action step)
        {
            try
            {
                step();
            }
            catch (Exception ex)
            {
                PluginLog.Warning($"Bunny automation cleanup step '{name}' failed: {ex.Message}");
            }
        }

        private static void StopInUnsupportedZone(string zoneName)
        {
            PluginLog.Information($"Bunny automation is not implemented for {zoneName}, disabling plugin.");
            P.ChatGui.Print($"[Eureka Suite] Bunny automation only supports Eureka Pyros. Stopped because you are in {zoneName}.");
            DisablePlugin();
        }

        internal static void Tick()
        {
            if (DoWeTick)
            {
                if (!P.taskManager.IsBusy)
                {
                    if (BunniesRun)
                    {
                        if (IsInZone(Pagos))
                        {
                            StopInUnsupportedZone("Eureka Pagos");
                        }
                        else if (IsInZone(Hydatos))
                        {
                            StopInUnsupportedZone("Eureka Hydatos");
                        }
                        else if (IsInZone(Pyros))
                        {
                            P.stopwatch.Start();
                            double timeElasped = P.stopwatch.Elapsed.TotalSeconds;
                            if (timeElasped > (C.hours * 3600 + C.minutes * 60) && !C.runInfinite && !HasBunnyStatus() && !IsInBunnyFate())
                            {
                                P.taskManager.Enqueue(() => UpdateCurrentTask("Leaving Duty"));
                                P.taskManager.Enqueue(LeaveDuty);
                                P.taskManager.Enqueue(() => IsInZone(Kugane) && PlayerNotBusy());
                                if (C.teleportToHouse)
                                    if (C.teleportToFC)
                                    {
                                        P.taskManager.Enqueue(() => UpdateCurrentTask("Going to FC"));
                                        TaskGoToFC.Enqueue();
                                    }
                                    else if (C.teleportToHouse)
                                    {
                                        P.taskManager.Enqueue(() => UpdateCurrentTask("Going to Personal"));
                                        TaskGoToHome.Enqueue();
                                    }
                                    else
                                        P.taskManager.Enqueue(() => PluginLog.LogInformation("Either House doesn't exist or the task failed."));

                                if (C.logoutAfter)
                                {
                                    P.taskManager.Enqueue(() => UpdateCurrentTask("Logging Out"));
                                    P.taskManager.Enqueue(() => RunCommand("logout"));
                                    P.taskManager.Enqueue(() => IsAddonActive("SelectYesno"));
                                    P.taskManager.Enqueue(() => GenericHandler.FireCallback("SelectYesno", true, 0));
                                }
                                P.taskManager.Enqueue(DisablePlugin);
                            }
                            else if (C.enableRetainers && ARRetainersWaitingToBeProcessed() && !HasBunnyStatus() && !IsInBunnyFate())
                            {
                                P.taskManager.Enqueue(() => UpdateCurrentTask("Resending Retainers"));
                                P.taskManager.Enqueue(LeaveDuty);
                                P.taskManager.Enqueue(() => CurrentZoneID() == Kugane);
                                P.taskManager.Enqueue(PlayerNotBusy);
                                P.taskManager.EnqueueDelay(1000);
                            }
                            else
                            {
                                // Go To Bunny Location
                                // Wait for Fate
                                // Do Fate
                                if (HasBunnyStatus())
                                {
                                    ToggleRotationAIOff();
                                    if (!IsPlayerAtBossLocation(Svc.Objects.LocalPlayer!.Position))
                                    {
                                        TaskPluginLog.Enqueue("Going to Boss Location");
                                        TaskMoveTo.Enqueue(new Vector3(161.120f, 710.682f, 259.266f), "Boss");
                                    }
                                    TaskMounting.Enqueue();
                                    P.taskManager.Enqueue(() => RunCommand("rsr off"));
                                    TaskPluginLog.Enqueue("Has Bunny");
                                    P.taskManager.Enqueue(() => PyrosMovementHandler.InitialStart());
                                }

                                else if (!HasBunnyStatus())
                                {
                                    if (C.enableRepair && NeedsRepair(C.repairSlider) && !IsInBunnyFate())
                                    {
                                        P.taskManager.Enqueue(() => PluginLog.Information("Need Repair"));
                                        if (C.selfRepair)
                                        {
                                            P.taskManager.Enqueue(() => UpdateCurrentTask("Self Repairing"));
                                            TaskDismount.Enqueue();
                                            TaskSelfRepair.Enqueue();
                                        }
                                        else
                                        {
                                            P.taskManager.Enqueue(() => UpdateCurrentTask("Going to Repair Mender"));
                                            TaskMounting.Enqueue();
                                            TaskMoveTo.Enqueue(PyrosRepairNpc, "Pyros Mender", 0.5f);
                                            TaskRepairNpc.Enqueue("Expedition Mender");
                                        }    
                                    }

                                    else if (IsInBunnyFate())
                                    {
                                        ToggleRotationAI();
                                        P.taskManager.Enqueue(Sync);
                                        TaskDismount.Enqueue();
                                        TaskPluginLog.Enqueue("Inside Bunny Fate");
                                        P.taskManager.Enqueue(() => UpdateCurrentTask("In Bunny Fate"));
                                        PyrosTargetingHandler.Enqueue();
                                        TaskMounting.Enqueue();
                                        P.taskManager.Enqueue(() => HasBunnyStatus());
                                        TaskPluginLog.Enqueue("Finidng Coffer");
                                    }

                                    else if (IsAtBunny())
                                    {
                                        P.taskManager.Enqueue(() => RunCommand("rsr off"));
                                        P.taskManager.Enqueue(() => UpdateCurrentTask("Waiting at Fate"));
                                        P.taskManager.EnqueueDelay(100);
                                    }

                                    else if (!IsAtBunny())
                                    {
                                        var x = RandomPointInTriangle(PyrosCenterFatePoint,PyrosRightFatePoint,PyrosLeftFatePoint);
                                        TaskMounting.Enqueue();
                                        TaskMoveTo.Enqueue(x, "Bunny Fate Location", 1f);
                                    }
                                }
                            }
                        }
                        else if (TryGetAddonByName<AtkUnitBase>("RetainerList", out var RetainerAddon) && IsAddonReady(RetainerAddon) && !ARRetainersWaitingToBeProcessed())
                        {
                            TaskGetOut.Enqueue();
                        }
                        else if (IsInZone(Kugane) && C.enableRetainers && ARRetainersWaitingToBeProcessed())
                        {
                            var closest = AethernetData.Distances.OrderBy(x => x.distance).First();
                            P.taskManager.Enqueue(() => UpdateCurrentTask("Resending Retainers"));
                            if (closest.position != AethernetKogane)
                                TaskUseAethernet.Enqueue("Kogane Dori Markets");
                            TaskMoveTo.Enqueue(SummoningBell, "SummoningBell", 1f);
                            TaskUseRetainer.Enqueue();
                        }
                        else if (IsInZone(Kugane))
                        {
                            UpdateCurrentTask("Going to Eureka");
                            var closest = AethernetData.Distances.OrderBy(x => x.distance).First();
                            if (closest.position != AethernetPier1)
                                TaskUseAethernet.Enqueue("Pier #1");
                            TaskMoveToKuganeNpc.Enqueue();
                            TaskTarget.Enqueue(KuganeNpcObjectID);
                            TaskInteract.Enqueue(KuganeNpcObjectID);
                            P.taskManager.Enqueue(() =>
                            {
                                if (C.zoneSelected == PyrosZoneIndex)
                                {
                                    UpdateCurrentTask("Entering Pyros");
                                    P.taskManager.Enqueue(() => GenericHandler.FireCallback("SelectString", true, 1));
                                    P.taskManager.Enqueue(() => GenericHandler.FireCallback("SelectYesno", true, 0));
                                    P.taskManager.EnqueueDelay(200);
                                    P.taskManager.Enqueue(() => GenericHandler.FireCallback("ContentsFinderConfirm", true, 8));
                                    P.taskManager.Enqueue(() => CurrentZoneID() == Pyros);
                                    P.taskManager.Enqueue(PlayerNotBusy);
                                    P.taskManager.Enqueue(P.navmesh.IsReady);
                                }
                                else
                                {
                                    PluginLog.Information($"Selected zone {C.zoneSelected} is not implemented, disabling plugin.");
                                    P.ChatGui.Print("[Eureka Suite] Bunny automation only supports Eureka Pyros. Stopped.");
                                    DisablePlugin();
                                }
                            });
                        }

                        else if (!IsInZone(Kugane))
                        {
                            TaskTeleportKugane.Enqueue();
                            if (C.enableRetainers && ARRetainersWaitingToBeProcessed())
                            {
                                P.taskManager.Enqueue(() => UpdateCurrentTask("Resending Retainers"));
                                TaskUseAethernet.Enqueue("Kogane Dori Markets");
                                TaskMoveTo.Enqueue(SummoningBell, "SummoningBell", 1f);
                                TaskUseRetainer.Enqueue();
                            }
                        }
                    }
                }
            }
        }
    }
}

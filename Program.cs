using System;
using System.Collections.Generic;
using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI.Ingame.Utilities;
using VRageMath;

namespace IngameScript
{
    public partial class Program : MyGridProgram
    {
        private string ProjectorTag;

        private IMyTextSurface Screen;

        private IMyProjector Projector;

        private int MaxSteps;

        bool Init()
        {
            // Get the screen surface and initialize it
            Screen = Me.GetSurface(0);
            Screen.ContentType = ContentType.TEXT_AND_IMAGE;
            Screen.WriteText("");

            ParseConfig();

            if (!FindProjector())
            {
                return false;
            }

            return true;
        }

        void Log(string message, MessageLevel level, bool append = true)
        {
            Screen.WriteText($"[{MessageLevelToString(level)}] {message}" + "\n", append: append);
        }

        void InitConfig(MyIni config)
        {
            config.Clear();
            config.AddSection("projector");
            config.Set("projector", "tag", "RPA");
            config.SetComment("projector", "tag", "The tag of the projector to align");
            config.AddSection("options");
            config.Set("options", "maxSteps", 10);
            config.SetComment("options", "maxSteps", "The maximum number of steps to take when aligning the projector");
            config.SetEndComment(@"
Repair projector alignment

This script will align the repair projector tagged with [RPA] with the grid

and ensure it is properly configured for repairs.

Rerun the script if you update the blueprint

Change the options above to fit your needs.
                ");
            Me.CustomData = config.ToString();
        }

        void ParseConfig()
        {
            var config = new MyIni();
            if (!config.TryParse(Me.CustomData))
            {
                InitConfig(config);
            }
            else if (!config.ContainsKey("projector", "tag"))
            {
                InitConfig(config);
            }

            ProjectorTag = config.Get("projector", "tag").ToString();
            MaxSteps = config.Get("options", "maxSteps").ToInt32(1000);
            Log($"Projector tag set to: [{ProjectorTag}]", MessageLevel.Info);
        }

        bool FindProjector()
        {
            var blocks = new List<IMyTerminalBlock>();
            GridTerminalSystem.SearchBlocksOfName($"[{ProjectorTag}]", blocks);

            if (blocks.Count == 1 && blocks[0] is IMyProjector)
            {
                Projector = blocks[0] as IMyProjector;
                Log($"Projector found: {Projector.CustomName}", MessageLevel.Info);
                return true;
            }
            Log($"Error: Expected one projector with tag [{ProjectorTag}], found {blocks.Count}.", MessageLevel.Error);
            return false;
        }

        int GetPreviousCompleteness()
        {
            var config = new MyIni();
            if (config.TryParse(Projector.CustomData))
            {
                return config.Get("projector", "completeness").ToInt32(0);
            }
            config.AddSection("projector");
            config.Set("projector", "completeness", GetCompleteness());
            config.Set("projector", "step", 0);
            config.Set("projector", "offsetX", 0);
            config.Set("projector", "offsetY", 0);
            config.Set("projector", "offsetZ", 0);
            config.Set("projector", "rotX", 0);
            config.Set("projector", "rotY", 0);
            config.Set("projector", "rotZ", 0);
            Projector.CustomData = config.ToString();
            return GetCompleteness();
        }

        int GetCurrentStep()
        {
            var config = new MyIni();
            if (config.TryParse(Projector.CustomData))
            {
                return config.Get("projector", "step").ToInt32(0);
            }
            return 0;
        }

        void NextStep(int value)
        {
            var config = new MyIni();
            if (config.TryParse(Projector.CustomData))
            {
                config.Set("projector", "step", value);
                config.Set("projector", "completeness", GetCompleteness());
                config.Set("projector", "offsetX", Projector.ProjectionOffset.X);
                config.Set("projector", "offsetY", Projector.ProjectionOffset.Y);
                config.Set("projector", "offsetZ", Projector.ProjectionOffset.Z);
                config.Set("projector", "rotX", Projector.ProjectionRotation.X);
                config.Set("projector", "rotY", Projector.ProjectionRotation.Y);
                config.Set("projector", "rotZ", Projector.ProjectionRotation.Z);
                Projector.CustomData = config.ToString();
            }
        }

        void ResetToPreviousOffsets()
        {
            var config = new MyIni();
            if (config.TryParse(Projector.CustomData))
            {
                Projector.ProjectionOffset = new Vector3I(
                    config.Get("projector", "offsetX").ToInt32(0),
                    config.Get("projector", "offsetY").ToInt32(0),
                    config.Get("projector", "offsetZ").ToInt32(0)
                );
                Projector.ProjectionRotation = new Vector3I(
                    config.Get("projector", "rotX").ToInt32(0),
                    config.Get("projector", "rotY").ToInt32(0),
                    config.Get("projector", "rotZ").ToInt32(0)
                );
                Projector.UpdateOffsetAndRotation();
                config.Set("projector", "step", GetCurrentStep() + 1);
                Projector.CustomData = config.ToString();
            }
        }

        int GetCompleteness()
        {
            if (Projector.RemainingBlocks <= 0)
            {
                return 0;
            }

            return Projector.TotalBlocks * 3 - Projector.BuildableBlocksCount - 2 * Projector.RemainingBlocks;
        }

        public Program()
        {
            Runtime.UpdateFrequency = UpdateFrequency.Update1;
        }

        public void Save()
        {
            // Called when the program needs to save its state.
        }

        public void Main(string argument, UpdateType updateSource)
        {
            if (!Init())
            {
                Log("Initialization failed.", MessageLevel.Error);
                return;
            }
            Log("Initialization complete.", MessageLevel.Info);

            Projector.Enabled = true;
            if (!Projector.IsWorking)
            {
                Log($"{Projector.CustomName} is not functional.", MessageLevel.Error);
                return;
            }
            Log($"{Projector.CustomName} is functional.", MessageLevel.Info);

            Projector.SetValue("KeepProjection", true);
            Projector.SetValue("OnOff", true);

            // Attempt to spawn projection
            Projector.ApplyAction("SpawnProjection");

            if (!Projector.IsProjecting)
            {
                Log($"Failed to spawn projection on [{Projector.CustomName}].", MessageLevel.Error);
                return;
            }
            Log($"Successfully spawned projection on [{Projector.CustomName}].", MessageLevel.Info);

            if (GetCompleteness() <= 0)
            {
                Log($"Projection on [{Projector.CustomName}] is aligned.", MessageLevel.Info);
                return;
            }

            Log($"Aligning projection on [{Projector.CustomName}]...", MessageLevel.Info);

            int step = GetCurrentStep();

            Log($"Step {step} of {MaxSteps}", MessageLevel.Info);

            if (step <= 0)
            {
                Projector.ProjectionOffset = new Vector3I(0, 0, 0);
                Projector.ProjectionRotation = new Vector3I(0, 0, 0);
                Projector.UpdateOffsetAndRotation();
                NextStep(0);
            }

            if (step > MaxSteps)
            {
                Log($"Max steps reached, projection on [{Projector.CustomName}] is not aligned.", MessageLevel.Warning);
                return;
            }

            Random random = new Random();

            // Randomly force accept
            int forceAccept = random.Next(100);

            Log($"Prev: {GetPreviousCompleteness()}, Curr: {GetCompleteness()}", MessageLevel.Info);
            if (GetPreviousCompleteness() > GetCompleteness() || forceAccept < 5)
            {
                Log("No rollback needed", MessageLevel.Info);
            }
            else
            {
                Log("Rollback needed", MessageLevel.Info);
                ResetToPreviousOffsets();
                return;
            }

            // Randomly pick axis: 0=X, 1=Y, 2=Z
            int axis = random.Next(3);

            // Randomly pick direction: -1 or 1
            int dir = random.Next(2) == 0 ? -1 : 1;

            // Randomly decide to change offset or rotation
            bool changeOffset = random.Next(100) < 95;

            if (changeOffset)
            {
                // Change offset
                switch (axis)
                {
                    case 0:
                        Projector.ProjectionOffset += new Vector3I(dir, 0, 0);
                        break;
                    case 1:
                        Projector.ProjectionOffset += new Vector3I(0, dir, 0);
                        break;
                    case 2:
                        Projector.ProjectionOffset += new Vector3I(0, 0, dir);
                        break;
                }
            }
            else
            {
                // Change rotation
                switch (axis)
                {
                    case 0:
                        Projector.ProjectionRotation += new Vector3I(dir * 90, 0, 0);
                        break;
                    case 1:
                        Projector.ProjectionRotation += new Vector3I(0, dir * 90, 0);
                        break;
                    case 2:
                        Projector.ProjectionRotation += new Vector3I(0, 0, dir * 90);
                        break;
                }
            }
            Projector.UpdateOffsetAndRotation();
            NextStep(step + 1);
        }

        private enum MessageLevel
        {
            Info,
            Warning,
            Error
        }

        private static string MessageLevelToString(MessageLevel level)
        {
            switch (level)
            {
                case MessageLevel.Info: return "Info";
                case MessageLevel.Warning: return "Warning";
                case MessageLevel.Error: return "Error";
                default: return level.ToString();
            }
        }
    }
}

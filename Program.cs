using System;
using System.Collections.Generic;
using System.Numerics;
using Sandbox.Game.Entities;
using Sandbox.Gui;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI.Ingame.Utilities;
using VRageMath;

namespace IngameScript
{
    public partial class Program : MyGridProgram
    {
        private IMyTextSurface Screen;
        private IMyProjector Projector;
        private string ProjectorTag;
        private int Step;
        private int Rotation;

        private void ParseConfig()
        {
            var config = new MyIni();
            config.TryParse(Me.CustomData);
            ProjectorTag = config.Get("Settings", "ProjectorTag").ToString("RPA");
            Step = config.Get("Internal", "Step").ToInt32(-1);
            Rotation = config.Get("Internal", "Rotation").ToInt32(-1);
        }

        private void SaveConfig()
        {
            var config = new MyIni();
            config.Set("Settings", "ProjectorTag", ProjectorTag);
            config.Set("Internal", "Step", Step);
            config.Set("Internal", "Rotation", Rotation);
            Me.CustomData = config.ToString();
        }

        private Vector3I GetNextBlock()
        {
            var minVec = Me.CubeGrid.Min;
            var maxVec = Me.CubeGrid.Max;
            var maxSteps = (maxVec.X - minVec.X + 1) * (maxVec.Y - minVec.Y + 1) * (maxVec.Z - minVec.Z + 1);

            Vector3I currentVec;
            Step--;
            do
            {
                // Get current vector from step (ie one step for every position in 3d grid)
                Step++;
                currentVec = new Vector3I(
                    minVec.X + (Step % (maxVec.X - minVec.X + 1)),
                    minVec.Y + (Step / (maxVec.X - minVec.X + 1) % (maxVec.Y - minVec.Y + 1)),
                    minVec.Z + (Step / ((maxVec.X - minVec.X + 1) * (maxVec.Y - minVec.Y + 1)) % (maxVec.Z - minVec.Z + 1))
                );
            } while (Me.CubeGrid.CubeExists(currentVec) && Step < maxSteps);

            if (Step >= maxSteps)
            {
                return new Vector3I(int.MaxValue, int.MaxValue, int.MaxValue);
            }
            return currentVec;
        }

        private Vector3I GetRotationFromIndex(int index)
        {
            // Unflatten to RotX, RotY, RotZ
            var RotX = (Rotation / 16) - 2;
            var RotY = (Rotation % 16 / 4) - 2;
            var RotZ = (Rotation % 4) - 2;
            return new Vector3I(RotX, RotY, RotZ);
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
            // Check if the update source is valid
            if (updateSource != UpdateType.Update100 && updateSource != UpdateType.Update10 && updateSource != UpdateType.Update1)
            {
                return;
            }

            // Get the screen surface and initialize it
            Screen = Me.GetSurface(0);
            Screen.ContentType = ContentType.TEXT_AND_IMAGE;
            Screen.WriteText("");

            // Parse the configuration
            ParseConfig();

            // Find the projector block
            var blocks = new List<IMyTerminalBlock>();
            GridTerminalSystem.SearchBlocksOfName($"[{ProjectorTag}]", blocks);
            if (blocks.Count != 1 || !(blocks[0] is IMyProjector))
            {
                Log($"Could not find projector with tag [{ProjectorTag}].", MessageLevel.Error, append: true);
                Halt();
                return;
            }
            Projector = blocks[0] as IMyProjector;

            // Increment rotation
            Rotation++;
            Rotation %= 64;

            // Increment step if all rotations are done
            if (Rotation == 0)
            {
                Step++;
            }

            // Init projector on first step
            if (Step == 0)
            {
                // Ensure the projector is functional
                if (!Projector.IsWorking)
                {
                    Log($"Projector [{ProjectorTag}] is not functional.", MessageLevel.Error, append: true);
                    Halt();
                    return;
                }

                // Start projection
                Projector.SetValue("KeepProjection", true);
                Projector.SetValue("OnOff", true);
                Projector.ApplyAction("SpawnProjection");

                if (!Projector.IsProjecting)
                {
                    Log($"Projector [{ProjectorTag}] failed to start projection.", MessageLevel.Error, append: true);
                    Halt();
                    return;
                }
            }

            if (Projector.RemainingBlocks == 0)
            {
                Log($"Projector [{ProjectorTag}] has completed projection.", MessageLevel.Info, append: true);
                Halt();
                return;
            }

            var minVec = Me.CubeGrid.Min;
            var maxVec = Me.CubeGrid.Max;

            // Get current vector from step (ie one step for every position in 3d grid)
            Vector3I currentVec = GetNextBlock();

            if (currentVec == new Vector3I(int.MaxValue, int.MaxValue, int.MaxValue))
            {
                Log($"Every combination tried and no match found", MessageLevel.Error, append: true);
                Halt();
                return;
            }

            Log($"Step {Step}: Projecting to {currentVec}", MessageLevel.Info, append: true);
            Projector.ProjectionOffset = currentVec;

            Log($"Trying Rotation {Rotation}", MessageLevel.Info, append: true);
            Projector.ProjectionRotation = GetRotationFromIndex(Rotation);

            // Update the projector's offset and rotation
            Projector.UpdateOffsetAndRotation();

            // Save the current state
            SaveConfig();
        }

        private enum MessageLevel
        {
            Info,
            Warning,
            Error
        }

        void Halt()
        {
            Runtime.UpdateFrequency = UpdateFrequency.None;
        }

        void Log(string message, MessageLevel level, bool append = true)
        {
            Screen.WriteText($"[{MessageLevelToString(level)}] {message}" + "\n", append: append);
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

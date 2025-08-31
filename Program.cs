using System;
using System.Collections.Generic;
using System.Numerics;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI.Ingame.Utilities;
using VRageMath;

namespace IngameScript
{
    public partial class Program : MyGridProgram
    {
        private float OffsetMutationRate;
        private int MaxStepsWithNoImprovementBeforeRotating;
        private float MaxFitnessThisRotation;
        private float ForceApplyRate;

        private IMyTextSurface Screen;
        private IMyProjector Projector;

        private string ProjectorTag;
        private int MaxSteps;

        // Current Values
        private int StepsSinceImprovement;
        private int Step;
        private float Fitness;
        private int OffsetX;
        private int OffsetY;
        private int OffsetZ;
        private int Rotation;

        // Previous Values
        private int PrevStepsSinceImprovement;
        private int PrevStep;
        private float PrevFitness;
        private int PrevOffsetX;
        private int PrevOffsetY;
        private int PrevOffsetZ;
        private int PrevRotation;


        void ParseConfig()
        {
            var config = new MyIni();
            config.TryParse(Me.CustomData);
            ProjectorTag = config.Get("Settings", "ProjectorTag").ToString("RPA");
            MaxSteps = config.Get("Settings", "MaxSteps").ToInt32(20000);
            OffsetMutationRate = config.Get("Settings", "OffsetMutationRate").ToSingle(0.15f);
            MaxStepsWithNoImprovementBeforeRotating = config.Get("Settings", "MaxStepsWithNoImprovementBeforeRotating").ToInt32(100);
            ForceApplyRate = config.Get("Settings", "ForceApplyRate").ToSingle(0.01f);
            PrevStep = config.Get("Internal", "PrevStep").ToInt32(0);
            PrevFitness = config.Get("Internal", "PrevFitness").ToSingle(0f);
            PrevOffsetX = config.Get("Internal", "PrevOffsetX").ToInt32(0);
            PrevOffsetY = config.Get("Internal", "PrevOffsetY").ToInt32(0);
            PrevOffsetZ = config.Get("Internal", "PrevOffsetZ").ToInt32(0);
            PrevRotation = config.Get("Internal", "PrevRotation").ToInt32(0);
            PrevStepsSinceImprovement = config.Get("Internal", "PrevStepsSinceImprovement").ToInt32(0);
            MaxFitnessThisRotation = config.Get("Internal", "MaxFitnessThisRotation").ToSingle(0f);
        }

        void SaveConfig()
        {
            var config = new MyIni();
            config.Set("Settings", "ProjectorTag", ProjectorTag);
            config.Set("Settings", "MaxSteps", MaxSteps);
            config.Set("Settings", "OffsetMutationRate", OffsetMutationRate);
            config.Set("Settings", "MaxStepsWithNoImprovementBeforeRotating", MaxStepsWithNoImprovementBeforeRotating);
            config.Set("Settings", "ForceApplyRate", ForceApplyRate);
            config.Set("Internal", "PrevStep", Step);
            config.Set("Internal", "PrevFitness", Fitness);
            config.Set("Internal", "PrevOffsetX", OffsetX);
            config.Set("Internal", "PrevOffsetY", OffsetY);
            config.Set("Internal", "PrevOffsetZ", OffsetZ);
            config.Set("Internal", "PrevRotation", Rotation);
            config.Set("Internal", "PrevStepsSinceImprovement", StepsSinceImprovement);
            config.Set("Internal", "MaxFitnessThisRotation", MaxFitnessThisRotation);
            Me.CustomData = config.ToString();
        }

        void UpdateOffsets()
        {
            Projector.ProjectionOffset = new Vector3I(OffsetX, OffsetY, OffsetZ);
            // Unflatten to RotX, RotY, RotZ
            var RotX = (Rotation / 16) - 2;
            var RotY = (Rotation % 16 / 4) - 2;
            var RotZ = (Rotation % 4) - 2;
            Projector.ProjectionRotation = new Vector3I(RotX, RotY, RotZ);
            Projector.UpdateOffsetAndRotation();
        }

        void FitnessFunction()
        {
            var complete = (Projector.TotalBlocks - Projector.RemainingBlocks) / (float)Projector.TotalBlocks;
            var weldable = 1 - (Projector.TotalBlocks - Projector.BuildableBlocksCount) / (float)Projector.TotalBlocks;

            if (complete >= 1)
            {
                Fitness = 1;
            } else if (weldable > complete)
            {
                Fitness = 0.2f * weldable + 0.8f * complete;
            }
            else
            {
                Fitness = complete;
            }
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
            // Reset if arg is RESET
            if (argument == "RESET")
            {
                ParseConfig();
                Step = 0;
                SaveConfig();
                Halt();
                return;
            }

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

            // Increment Step
            Step = PrevStep + 1;

            // Log Current Step
            Log($"Step {Step} of {MaxSteps}.", MessageLevel.Info, append: false);

            // Abort if exceeded max steps
            if (Step > MaxSteps)
            {
                Log($"Exceeded {MaxSteps} Steps, aborting.", MessageLevel.Error, append: true);
                Halt();
                return;
            }

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

            if (PrevStep == 0)
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

                OffsetX = PrevOffsetX;
                OffsetY = PrevOffsetY;
                OffsetZ = PrevOffsetZ;
                Rotation = PrevRotation;

                FitnessFunction();
                UpdateOffsets();
                SaveConfig();
                return;
            }

            // Calculate Current Fitness
            FitnessFunction();
            if (Fitness >= 1f)
            {
                Log("Success!", MessageLevel.Info);
                Log($"Projector Aligned in {Step} Steps.", MessageLevel.Info);
                Halt();
                return;
            }
            Log($"Fitness: {Fitness}", MessageLevel.Info);
            Random random = new Random();

            if (Fitness > PrevFitness || random.NextDouble() < ForceApplyRate)
            {
                OffsetX = random.NextDouble() < OffsetMutationRate ? OffsetX + 1 : OffsetX;
                OffsetX = random.NextDouble() < OffsetMutationRate ? OffsetX - 1 : OffsetX;
                OffsetY = random.NextDouble() < OffsetMutationRate ? OffsetY + 1 : OffsetY;
                OffsetY = random.NextDouble() < OffsetMutationRate ? OffsetY - 1 : OffsetY;
                OffsetZ = random.NextDouble() < OffsetMutationRate ? OffsetZ + 1 : OffsetZ;
                OffsetZ = random.NextDouble() < OffsetMutationRate ? OffsetZ - 1 : OffsetZ;
            }
            else
            {
                OffsetX = PrevOffsetX;
                OffsetY = PrevOffsetY;
                OffsetZ = PrevOffsetZ;
            }

            StepsSinceImprovement = PrevStepsSinceImprovement + 1;
            if (MaxFitnessThisRotation < Fitness)
            {
                MaxFitnessThisRotation = Fitness;
                StepsSinceImprovement = 0;
            }

            if (StepsSinceImprovement > MaxStepsWithNoImprovementBeforeRotating)
            {
                Rotation = (PrevRotation + 1) % 64;
                MaxFitnessThisRotation = 0;
                StepsSinceImprovement = 0;
            }

            Log($"Max Fitness This Rotation: {MaxFitnessThisRotation}", MessageLevel.Info);
            Log($"Steps Since Improvement: {StepsSinceImprovement}", MessageLevel.Info);
            Log($"Offset: {OffsetX}, {OffsetY}, {OffsetZ}, {Rotation}", MessageLevel.Info);
            UpdateOffsets();
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

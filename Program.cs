using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.Remoting.Metadata;
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
    // PITCH=X YAW=Y ROLL=Z
    private static readonly List<Vector3I> Rotations = new List<Vector3I>
    {
      new Vector3I(0, 0, 0), // Forward upright
      new Vector3I(0, 0, 1), // Forward rolled left
      new Vector3I(0, 0, -1), // Forward rolled right
      new Vector3I(0, 0, 2), // Forward upside down
      new Vector3I(1, 0, 0), // Left upright
      new Vector3I(1, 0, 1), // Left rolled left
      new Vector3I(1, 0, -1), // Left rolled right
      new Vector3I(1, 0, 2), // Left upside down
      new Vector3I(-1, 0, 0), // Right upright
      new Vector3I(-1, 0, 1), // Right rolled left
      new Vector3I(-1, 0, -1), // Right rolled right
      new Vector3I(-1, 0, 2), // Right upside down
      new Vector3I(2, 0, 0), // Backwards upright
      new Vector3I(2, 0, 1), // Backwards rolled left
      new Vector3I(2, 0, -1), // Backwards rolled right
      new Vector3I(2, 0, 2), // Backwards upside down
      new Vector3I(0, 1, 0), // Up upright
      new Vector3I(0, 1, 1), // Up rolled left
      new Vector3I(0, 1, -1), // Up rolled right
      new Vector3I(0, 1, 2), // Up upside down
      new Vector3I(0, -1, 0), // Down upright
      new Vector3I(0, -1, 1), // Down rolled left
      new Vector3I(0, -1, -1), // Down rolled right
      new Vector3I(0, -1, 2), // Down upside down
    };

    private IMyTextSurface Screen;
    private IMyProjector Projector;
    private string ProjectorTag;
    private List<Vector3I> GridCubes;
    private int Step;
    private int Rotation;

    private int InitProjector(int Step)
    {
      // Find the projector block
      var blocks = new List<IMyTerminalBlock>();
      GridTerminalSystem.SearchBlocksOfName($"[{ProjectorTag}]", blocks);
      if (blocks.Count != 1 || !(blocks[0] is IMyProjector))
      {
        Log($"Could not find projector with tag [{ProjectorTag}].", MessageLevel.Error, append: true);
        return 1;
      }
      Projector = blocks[0] as IMyProjector;

      if (Step <= 0)
      {
        // Start projection
        Projector.SetValue("KeepProjection", true);
        Projector.SetValue("OnOff", true);
        Projector.ApplyAction("SpawnProjection");

        // Ensure the projector is functional
        if (!Projector.IsWorking)
        {
          Log($"Projector [{ProjectorTag}] is not functional.", MessageLevel.Error, append: true);
          return 1;
        }

        if (!Projector.IsProjecting)
        {
          Log($"Failed to start projection.", MessageLevel.Error, append: true);
          Log("Please ensure the projector has a valid blueprint.", MessageLevel.Error, append: true);
          return 1;
        }
      }

      return 0;
    }

    private void InitScreen()
    {
      // Get the screen surface and initialize it
      Screen = Me.GetSurface(0);
      Screen.ContentType = ContentType.TEXT_AND_IMAGE;
      // Clear the screen
      Screen.WriteText("");
    }

    private void ParseCustomData()
    {
      var config = new MyIni();
      config.TryParse(Me.CustomData);
      ProjectorTag = config.Get("Settings", "ProjectorTag").ToString("RPA");
      Step = config.Get("Internal", "Step").ToInt32(0);
      Rotation = config.Get("Internal", "Rotation").ToInt32(0);
      GridCubes = new List<string>(config.Get("Internal", "GridCubes").ToString("").Split(';'))
        .Where(s => !string.IsNullOrWhiteSpace(s))
        .Select(s => s.Split(','))
        .Select(s => new Vector3I(int.Parse(s[0]), int.Parse(s[1]), int.Parse(s[2])))
        .ToList();
    }

    private void SaveCustomData()
    {
      var config = new MyIni();
      config.Set("Settings", "ProjectorTag", ProjectorTag);
      config.SetComment("Settings", "ProjectorTag", "\nThe tag used to identify the repair projector from it's name\nie: 'My Projector [RPA]'.\n");
      config.SetSectionComment("Settings", "\nConfiguration settings for this script\nPlease only modify this section.\n");
      config.Set("Internal", "Step", Step);
      config.Set("Internal", "Rotation", Rotation);
      config.Set("Internal", "GridCubes", string.Join(";", GridCubes.Select(c => $"{c.X},{c.Y},{c.Z}")));
      config.SetSectionComment("Internal", "\nInternal variables used by the script\nDo not modify.\n");
      Me.CustomData = config.ToString();
    }

    private void ScanGrid()
    {
      // Get the offset of the projector block
      var projectorCorrection = -Projector.Position;

      var min = Me.CubeGrid.Min;
      var max = Me.CubeGrid.Max;
      GridCubes = new List<Vector3I>();

      for (int x = min.X; x <= max.X; x++)
      {
        for (int y = min.Y; y <= max.Y; y++)
        {
          for (int z = min.Z; z <= max.Z; z++)
          {
            var candidate = new Vector3I(x, y, z);
            if (Me.CubeGrid.CubeExists(candidate))
            {
              if (Me.CubeGrid.GetCubeBlock(candidate) != null)
              {
                GridCubes.Add(Me.CubeGrid.GetCubeBlock(candidate).Position + projectorCorrection);
              }
            }
          }
        }
      }

      // Remove duplicates and sort by distance from origin (grid pivot)
      GridCubes = GridCubes.Distinct().ToList();
      GridCubes.Sort((a, b) => (a - projectorCorrection).Length().CompareTo((b - projectorCorrection).Length()));
    }

    public Program()
    {
      Runtime.UpdateFrequency = UpdateFrequency.Update100;
    }

    public void Save()
    {
      // Called when the program needs to save its state.
    }

    public void Main(string argument, UpdateType updateSource)
    {
      // Parse the configuration
      ParseCustomData();

      // Initialize the screen
      InitScreen();

      // Initialize the projector
      if (InitProjector(Step) > 0)
      {
        Halt();
        return;
      }

      // Build the GridCubes object
      if (GridCubes == null || GridCubes.Count == 0)
      {
        Log("Scanning grid for blocks...", MessageLevel.Info, append: true);
        ScanGrid();
        Log($"Found {GridCubes.Count} blocks.", MessageLevel.Info, append: true);
      }

      Log($"Step: {Step} of {GridCubes.Count}", MessageLevel.Info, append: true);
      Log($"Remaining Blocks: {Projector.RemainingBlocks}", MessageLevel.Info, append: true);

      if (Projector.RemainingBlocks <= 0)
      {
        Log("Successfully aligned projection.", MessageLevel.Info, append: true);
        Log($"X:{Projector.ProjectionOffset.X}, Y:{Projector.ProjectionOffset.Y}, Z:{Projector.ProjectionOffset.Z}", MessageLevel.Info, append: true);
        Log($"Pitch:{Projector.ProjectionRotation.X * 90}, Yaw:{Projector.ProjectionRotation.Y * 90}, Roll:{Projector.ProjectionRotation.Z * 90}", MessageLevel.Info, append: true);
        Halt();
        return;
      }

      // Deduce current offset and rotation to try
      var currentOffset = GridCubes[Step];
      var currentPitchYawRoll = Rotations[Rotation];

      // Setup next iteration
      Rotation++;
      if (Rotation >= Rotations.Count)
      {
        Rotation = 0;
        Step++;
      }

      if (Step > GridCubes.Count)
      {
        Log("Could not find a suitable position for projection.", MessageLevel.Warning, append: true);
        Log("Please ensure the projector has a recent blueprint of the current grid.", MessageLevel.Warning, append: true);
        Halt();
        return;
      }

      // Update projector position and rotation
      Projector.ProjectionOffset = currentOffset;
      Projector.ProjectionRotation = currentPitchYawRoll;
      Projector.UpdateOffsetAndRotation();
      SaveCustomData();
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

﻿using HavenSoft.HexManiac.Core.Models.Runs.Sprites;
using System.Collections.Generic;
using System.Diagnostics;

namespace HavenSoft.HexManiac.Core.Models.Runs.Factory {
   public abstract class RunStrategy {
      public string Format { get; set; }

      /// <summary>
      /// If a 'default' run is created for the pointer at the given address, how many bytes need to be available at the destination location?
      /// </summary>
      public abstract int LengthForNewRun(IDataModel model, int pointerAddress);

      /// <summary>
      /// Returns true if the format is capable of being added for the pointer at source.
      /// If the token is such that edits are allowed, actually add the format.
      /// </summary>
      public abstract bool TryAddFormatAtDestination(IDataModel owner, ModelDelta token, int source, int destination, string name, IReadOnlyList<ArrayRunElementSegment> sourceSegments);

      /// <summary>
      /// Returns true if the input run is valid for this run 'type'.
      /// Often this is just a type comparison, but for runs with multiple
      /// formats (example, SpriteRun with width/height), it can be more complex.
      /// </summary>
      public abstract bool Matches(IFormattedRun run);

      /// <summary>
      /// Create a new run meant to go into a pointer in a table.
      /// The destination has been prepared, but is all FF.
      /// </summary>
      public abstract IFormattedRun WriteNewRun(IDataModel owner, ModelDelta token, int source, int destination, string name, IReadOnlyList<ArrayRunElementSegment> sourceSegments);

      /// <summary>
      /// A pointer format in a table has changed.
      /// Replace the given run with a new run of the appropriate format.
      /// </summary>
      public abstract void UpdateNewRunFromPointerFormat(IDataModel model, ModelDelta token, string name, ref IFormattedRun run);

      /// <summary>
      /// Attempt to parse the format into a reasonable run that starts at the specified dataIndex.
      /// </summary>
      public abstract ErrorInfo TryParseFormat(IDataModel model, string name, int dataIndex, ref IFormattedRun run);

      protected ITableRun GetTable(IDataModel model, int pointerAddress) => (ITableRun)model.GetNextRun(pointerAddress);

      protected ArrayRunPointerSegment GetSegment(ITableRun table, int pointerAddress) {
         var offsets = table.ConvertByteOffsetToArrayOffset(pointerAddress);
         return (ArrayRunPointerSegment)table.ElementContent[offsets.SegmentIndex];
      }
   }

   public class FormatRunFactory {
      public static RunStrategy GetStrategy(string format) {
         RunStrategy strategy = null;
         if (format == PCSRun.SharedFormatString) {
            strategy = new PCSRunContentStrategy();
         } else if (format.StartsWith(AsciiRun.SharedFormatString)) {
            strategy = new AsciiRunContentStrategy();
         } else if (format == EggMoveRun.SharedFormatString) {
            strategy = new EggRunContentStrategy();
         } else if (format == PLMRun.SharedFormatString) {
            strategy = new PLMRunContentStrategy();
         } else if (format == TrainerPokemonTeamRun.SharedFormatString) {
            strategy = new TrainerPokemonTeamRunContentStrategy();
         } else if (LzSpriteRun.TryParseSpriteFormat(format, out var spriteFormat)) {
            strategy = new LzSpriteRunContentStrategy(spriteFormat);
         } else if (LzPaletteRun.TryParsePaletteFormat(format, out var paletteFormat)) {
            strategy = new LzPaletteRunContentStrategy(paletteFormat);
         } else if (SpriteRun.TryParseSpriteFormat(format, out var spriteFormat1)) {
            strategy = new SpriteRunContentStrategy(spriteFormat1);
         } else if (PaletteRun.TryParsePaletteFormat(format, out var paletteFormat1)) {
            strategy = new PaletteRunContentStrategy(paletteFormat1);
         } else if (format.IndexOf("[") >= 0 && format.IndexOf("[") < format.IndexOf("]")) {
            strategy = new TableStreamRunContentStrategy();
         } else {
            Debug.Fail("Not Implemented!");
            return null;
         }

         strategy.Format = format;
         return strategy;
      }
   }
}

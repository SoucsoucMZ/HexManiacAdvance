﻿using HavenSoft.HexManiac.Core.Models;
using HavenSoft.HexManiac.Core.Models.Runs;
using HavenSoft.HexManiac.Core.Models.Runs.Sprites;
using HavenSoft.HexManiac.Core.ViewModels.DataFormats;
using System;
using System.Collections.ObjectModel;
using System.Linq;

namespace HavenSoft.HexManiac.Core.ViewModels.Tools {
   public class TableGroupViewModel : ViewModelCore {

      private int currentMember; // used with open/close when refreshing the collection

      private string groupName;
      public bool DisplayHeader => GroupName != "Other";
      public string GroupName { get => groupName; set => Set(ref groupName, value, old => NotifyPropertyChanged(nameof(DisplayHeader))); }

      public ObservableCollection<IArrayElementViewModel> Members { get; } = new();

      public Action<IStreamArrayElementViewModel> ForwardModelChanged { get; init; }
      public Action<IStreamArrayElementViewModel> ForwardModelDataMoved { get; init; }

      public TableGroupViewModel() { GroupName = "Other"; }

      public void Open() => currentMember = 0;

      public void Add(IArrayElementViewModel child) {
         if (currentMember == Members.Count) {
            Members.Add(child);
         } else if (!Members[currentMember].TryCopy(child)) {
            Members[currentMember] = child;
         }
         currentMember += 1;
      }

      public void Close() {
         while (Members.Count > currentMember) Members.RemoveAt(Members.Count - 1);
      }

      public void AddChildrenFromTable(ViewPort viewPort, Selection selection, ITableRun table, int index, int splitPortion = -1) {
         var itemAddress = table.Start + table.ElementLength * index;
         var currentPartition = 0;
         foreach (var itemSegment in table.ElementContent) {
            var item = itemSegment;
            if (item is ArrayRunRecordSegment recordItem) item = recordItem.CreateConcrete(viewPort.Model, itemAddress);

            if (itemSegment is ArrayRunSplitterSegment) {
               currentPartition += 1;
               continue;
            } else if (splitPortion != -1 && splitPortion != currentPartition) {
               itemAddress += item.Length;
               continue;
            }

            IArrayElementViewModel viewModel = null;
            if (item.Type == ElementContentType.Unknown) viewModel = new FieldArrayElementViewModel(viewPort, item.Name, itemAddress, item.Length, HexFieldStratgy.Instance);
            else if (item.Type == ElementContentType.PCS) viewModel = new FieldArrayElementViewModel(viewPort, item.Name, itemAddress, item.Length, new TextFieldStrategy());
            else if (item.Type == ElementContentType.Pointer) viewModel = new FieldArrayElementViewModel(viewPort, item.Name, itemAddress, item.Length, new AddressFieldStratgy());
            else if (item.Type == ElementContentType.BitArray) viewModel = new BitListArrayElementViewModel(selection, viewPort.ChangeHistory, viewPort.Model, item.Name, itemAddress);
            else if (item.Type == ElementContentType.Integer) {
               if (item is ArrayRunEnumSegment enumSegment) {
                  viewModel = new ComboBoxArrayElementViewModel(viewPort, selection, item.Name, itemAddress, item.Length);
                  var anchor = viewPort.Model.GetAnchorFromAddress(-1, table.Start);
                  var enumSourceTableStart = viewPort.Model.GetAddressFromAnchor(new NoDataChangeDeltaModel(), -1, enumSegment.EnumName);
                  if (!string.IsNullOrEmpty(anchor) && viewPort.Model.GetDependantArrays(anchor).Count() == 1 && enumSourceTableStart >= 0) {
                     Add(viewModel);
                     viewModel = new BitListArrayElementViewModel(selection, viewPort.ChangeHistory, viewPort.Model, item.Name, itemAddress);
                  }
               } else if (item is ArrayRunTupleSegment tupleItem) {
                  viewModel = new TupleArrayElementViewModel(viewPort, tupleItem, itemAddress);
               } else if (item is ArrayRunHexSegment) {
                  viewModel = new FieldArrayElementViewModel(viewPort, item.Name, itemAddress, item.Length, HexFieldStratgy.Instance);
               } else if (item is ArrayRunColorSegment) {
                  viewModel = new ColorFieldArrayElementViewModel(viewPort, item.Name, itemAddress);
               } else if (item is ArrayRunCalculatedSegment calcSeg) {
                  viewModel = new CalculatedElementViewModel(viewPort, calcSeg, itemAddress);
               } else {
                  viewModel = new FieldArrayElementViewModel(viewPort, item.Name, itemAddress, item.Length, new NumericFieldStrategy());
               }
            } else {
               throw new NotImplementedException();
            }
            if (!SkipElement(item)) {
               Add(viewModel);
               AddChildrenFromPointerSegment(viewPort, itemAddress, item, currentMember - 1, recursionLevel: 0);
            }
            itemAddress += item.Length;
         }
      }

      private void AddChildrenFromPointerSegment(ViewPort viewPort, int itemAddress, ArrayRunElementSegment item, int parentIndex, int recursionLevel) {
         if (!(item is ArrayRunPointerSegment pointerSegment)) return;
         if (pointerSegment.InnerFormat == string.Empty) return;
         var destination = viewPort.Model.ReadPointer(itemAddress);
         IFormattedRun streamRun = null;
         if (destination != Pointer.NULL) {
            streamRun = viewPort.Model.GetNextRun(destination);
            if (!pointerSegment.DestinationDataMatchesPointerFormat(viewPort.Model, new NoDataChangeDeltaModel(), itemAddress, destination, null, parentIndex)) streamRun = null;
            if (streamRun != null && streamRun.Start != destination) {
               // For some reason (possibly because of a run length conflict),
               //    the destination data appears to match the expected type,
               //    but there is no run for it.
               // Go ahead and generate a new temporary run for the data.
               var strategy = viewPort.Model.FormatRunFactory.GetStrategy(pointerSegment.InnerFormat);
               strategy.TryParseData(viewPort.Model, string.Empty, destination, ref streamRun);
            }
         }

         IStreamArrayElementViewModel streamElement = null;
         if (streamRun == null || streamRun is IStreamRun) streamElement = new TextStreamElementViewModel(viewPort, itemAddress, pointerSegment.InnerFormat);
         if (streamRun is ISpriteRun spriteRun) streamElement = new SpriteElementViewModel(viewPort, spriteRun.FormatString, spriteRun.SpriteFormat, itemAddress);
         if (streamRun is IPaletteRun paletteRun) streamElement = new PaletteElementViewModel(viewPort, viewPort.ChangeHistory, paletteRun.FormatString, paletteRun.PaletteFormat, itemAddress);
         if (streamRun is TrainerPokemonTeamRun tptRun) streamElement = new TrainerPokemonTeamElementViewModel(viewPort, tptRun, itemAddress);
         if (streamElement == null) return;

         var streamAddress = itemAddress;
         var myIndex = currentMember;
         Members[parentIndex].DataChanged += (sender, e) => {
            var closure_destination = viewPort.Model.ReadPointer(streamAddress);
            var run = viewPort.Model.GetNextRun(closure_destination) as IStreamRun;
            IStreamArrayElementViewModel newStream = null;

            if (run == null || run is IStreamRun) newStream = new TextStreamElementViewModel(viewPort, streamAddress, pointerSegment.InnerFormat);
            if (run is ISpriteRun spriteRun1) newStream = new SpriteElementViewModel(viewPort, spriteRun1.FormatString, spriteRun1.SpriteFormat, streamAddress);
            if (run is IPaletteRun paletteRun1) newStream = new PaletteElementViewModel(viewPort, viewPort.ChangeHistory, paletteRun1.FormatString, paletteRun1.PaletteFormat, streamAddress);

            ForwardModelChanged(newStream);
            ForwardModelDataMoved(newStream);
            //newStream.DataChanged += ForwardModelChanged;
            //newStream.DataMoved += (sender, e) => ForwardModelDataMoved(sender, e);
            if (!Members[myIndex].TryCopy(newStream)) Members[myIndex] = newStream;
         };
         ForwardModelDataMoved(streamElement);
         //streamElement.DataMoved += (sender, e) => ForwardModelDataMoved(sender, e);
         Add(streamElement);

         parentIndex = currentMember - 1;
         if (streamRun is ITableRun tableRun && recursionLevel < 1) {
            int segmentOffset = 0;
            for (int i = 0; i < tableRun.ElementContent.Count; i++) {
               if (!(tableRun.ElementContent[i] is ArrayRunPointerSegment)) { segmentOffset += tableRun.ElementContent[i].Length; continue; }
               for (int j = 0; j < tableRun.ElementCount; j++) {
                  itemAddress = tableRun.Start + segmentOffset + j * tableRun.ElementLength;
                  AddChildrenFromPointerSegment(viewPort, itemAddress, tableRun.ElementContent[i], parentIndex, recursionLevel + 1);
               }
               segmentOffset += tableRun.ElementContent[i].Length;
            }
         }
      }

      private static bool SkipElement(ArrayRunElementSegment element) {
         return element.Name.StartsWith("unused") || element.Name.StartsWith("padding");
      }
   }
}
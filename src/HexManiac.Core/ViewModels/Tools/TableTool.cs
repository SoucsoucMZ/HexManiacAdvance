﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Windows.Input;
using HavenSoft.HexManiac.Core.Models;
using HavenSoft.HexManiac.Core.Models.Runs;
using HavenSoft.HexManiac.Core.ViewModels.DataFormats;

namespace HavenSoft.HexManiac.Core.ViewModels.Tools {
   public class TableTool : ViewModelCore, IToolViewModel {
      private readonly IDataModel model;
      private readonly Selection selection;
      private readonly ChangeHistory<ModelDelta> history;
      private readonly IToolTrayViewModel toolTray;

      public string Name => "Table";

      public IEnumerable<string> TableList => model.Arrays.Select(array => model.GetAnchorFromAddress(-1, array.Start));

      private int selectedTableIndex;
      public int SelectedTableIndex {
         get => selectedTableIndex;
         set {
            if (TryUpdate(ref selectedTableIndex, value)) {
               if (selectedTableIndex == -1) return;
               var array = model.Arrays.Skip(selectedTableIndex).First();
               selection.GotoAddress(array.Start);
               Address = array.Start;
            }
         }
      }

      private string currentElementName;
      public string CurrentElementName {
         get => currentElementName;
         set => TryUpdate(ref currentElementName, value);
      }

      private readonly StubCommand previous, next, append;
      public ICommand Previous => previous;
      public ICommand Next => next;
      public ICommand Append => append;
      private void CommandCanExecuteChanged() {
         previous.CanExecuteChanged.Invoke(previous, EventArgs.Empty);
         next.CanExecuteChanged.Invoke(next, EventArgs.Empty);
         append.CanExecuteChanged.Invoke(append, EventArgs.Empty);
      }

      public ObservableCollection<IArrayElementViewModel> Children { get; }

      // the address is the address not of the entire array, but of the current index of the array
      private int address = Pointer.NULL;
      public int Address {
         get => address;
         set {
            if (TryUpdate(ref address, value)) {
               var run = model.GetNextRun(value);
               if (run.Start > value || !(run is ArrayRun array)) {
                  Enabled = false;
                  CommandCanExecuteChanged();
                  return;
               }

               CommandCanExecuteChanged();
               Enabled = true;
               toolTray.Schedule(DataForCurrentRunChanged);
            }
         }
      }

      private bool enabled;
      public bool Enabled {
         get => enabled;
         private set => TryUpdate(ref enabled, value);
      }

#pragma warning disable 0067 // it's ok if events are never used after implementing an interface
      public event EventHandler<IFormattedRun> ModelDataChanged;
      public event EventHandler<string> OnError;
      public event EventHandler<string> OnMessage;
      public event EventHandler RequestMenuClose;
      public event EventHandler<(int originalLocation, int newLocation)> ModelDataMoved; // invoke when a new item gets added and the table has to move
#pragma warning restore 0067

      public TableTool(IDataModel model, Selection selection, ChangeHistory<ModelDelta> history, IToolTrayViewModel toolTray) {
         this.model = model;
         this.selection = selection;
         this.history = history;
         this.toolTray = toolTray;
         Children = new ObservableCollection<IArrayElementViewModel>();

         previous = new StubCommand {
            CanExecute = parameter => {
               var array = model.GetNextRun(address) as ArrayRun;
               return array != null && array.Start < address;
            },
            Execute = parameter => {
               var array = (ArrayRun)model.GetNextRun(address);
               selection.SelectionStart = selection.Scroll.DataIndexToViewPoint(Address - array.ElementLength);
               selection.SelectionEnd = selection.Scroll.DataIndexToViewPoint(selection.Scroll.ViewPointToDataIndex(selection.SelectionStart) + array.ElementLength - 1);
            }
         };

         next = new StubCommand {
            CanExecute = parameter => {
               var array = model.GetNextRun(address) as ArrayRun;
               return array != null && array.Start + array.Length > address + array.ElementLength;
            },
            Execute = parameter => {
               var array = (ArrayRun)model.GetNextRun(address);
               selection.SelectionStart = selection.Scroll.DataIndexToViewPoint(Address + array.ElementLength);
               selection.SelectionEnd = selection.Scroll.DataIndexToViewPoint(selection.Scroll.ViewPointToDataIndex(selection.SelectionStart) + array.ElementLength - 1);
            }
         };

         append = new StubCommand {
            CanExecute = parameter => {
               var array = model.GetNextRun(address) as ArrayRun;
               return array != null && array.Start + array.Length == address + array.ElementLength;
            },
            Execute = parameter => {
               using (ModelCacheScope.CreateScope(model)) {
                  var array = (ArrayRun)model.GetNextRun(address);
                  var originalArray = array;
                  var error = model.CompleteArrayExtension(history.CurrentChange, ref array);
                  if (array.Start != originalArray.Start) {
                     ModelDataMoved?.Invoke(this, (originalArray.Start, array.Start));
                     selection.GotoAddress(array.Start + array.Length - array.ElementLength);
                  }
                  if (error.HasError && !error.IsWarning) {
                     OnError?.Invoke(this, error.ErrorMessage);
                  } else {
                     if (error.IsWarning) OnMessage?.Invoke(this, error.ErrorMessage);
                     ModelDataChanged?.Invoke(this, array);
                     selection.SelectionStart = selection.Scroll.DataIndexToViewPoint(array.Start + array.Length - array.ElementLength);
                     selection.SelectionEnd = selection.Scroll.DataIndexToViewPoint(selection.Scroll.ViewPointToDataIndex(selection.SelectionStart) + array.ElementLength - 1);
                  }
                  RequestMenuClose?.Invoke(this, EventArgs.Empty);
               }
            }
         };

         CurrentElementName = "The Table tool only works if your cursor is on table data.";
      }

      public void DataForCurrentRunChanged() {
         foreach (var child in Children) child.DataChanged -= ForwardModelChanged;
         Children.Clear();

         var array = model.GetNextRun(Address) as ArrayRun;
         if (array == null) {
            CurrentElementName = "The Table tool only works if your cursor is on table data.";
            return;
         }

         NotifyPropertyChanged(nameof(TableList));
         TryUpdate(ref selectedTableIndex, model.Arrays.IndexOf(array), nameof(SelectedTableIndex));

         var basename = model.GetAnchorFromAddress(-1, array.Start);
         var index = (Address - array.Start) / array.ElementLength;
         if (array.ElementNames.Count > index) {
            CurrentElementName = $"{basename}/{index}" + Environment.NewLine + $"{basename}/{array.ElementNames[index]}";
         } else {
            CurrentElementName = $"{basename}/{index}";
         }

         if (!string.IsNullOrEmpty(array.LengthFromAnchor)) basename = array.LengthFromAnchor; // basename is now a 'parent table' name, if there is one

         AddChildrenFromTable(array, index);
         foreach(var currentArray in model.Arrays) {
            if (currentArray == array) continue;
            var currentArrayName = model.GetAnchorFromAddress(-1, currentArray.Start);
            if (currentArray.LengthFromAnchor == basename || currentArrayName == basename) {
               Children.Add(new SplitterArrayElementViewModel(currentArrayName));
               AddChildrenFromTable(currentArray, index);
            }
         }
      }

      private void AddChildrenFromTable(ArrayRun table, int index) {
         var itemAddress = table.Start + table.ElementLength * index;
         foreach (var item in table.ElementContent) {
            IArrayElementViewModel viewModel = null;
            if (item.Type == ElementContentType.Unknown) viewModel = new FieldArrayElementViewModel(history, model, item.Name, itemAddress, item.Length, new HexFieldStratgy());
            else if (item.Type == ElementContentType.PCS) viewModel = new FieldArrayElementViewModel(history, model, item.Name, itemAddress, item.Length, new TextFieldStratgy());
            else if (item.Type == ElementContentType.Pointer) viewModel = new FieldArrayElementViewModel(history, model, item.Name, itemAddress, item.Length, new AddressFieldStratgy());
            else if (item.Type == ElementContentType.BitArray) viewModel = new BitListArrayElementViewModel(selection, history, model, item.Name, itemAddress);
            else if (item.Type == ElementContentType.Integer) {
               if (item is ArrayRunEnumSegment enumSegment) {
                  viewModel = new ComboBoxArrayElementViewModel(selection, history, model, item.Name, itemAddress, item.Length);
                  var anchor = model.GetAnchorFromAddress(-1, table.Start);
                  if (!string.IsNullOrEmpty(anchor) && model.GetDependantArrays(anchor).Count() == 1) {
                     Children.Add(viewModel);
                     viewModel.DataChanged += ForwardModelChanged;
                     viewModel = new BitListArrayElementViewModel(selection, history, model, item.Name, itemAddress);
                  }
               } else {
                  viewModel = new FieldArrayElementViewModel(history, model, item.Name, itemAddress, item.Length, new NumericFieldStrategy());
               }
            } else {
               throw new NotImplementedException();
            }
            Children.Add(viewModel);
            viewModel.DataChanged += ForwardModelChanged;
            if (item is ArrayRunPointerSegment pointerSegment) {
               var destination = model.ReadPointer(itemAddress);
               if (destination != Pointer.NULL && model.GetNextRun(destination) is IStreamRun && pointerSegment.DestinationDataMatchesPointerFormat(model, new NoDataChangeDeltaModel(), destination)) {
                  if (pointerSegment.InnerFormat == PCSRun.SharedFormatString || pointerSegment.InnerFormat == PLMRun.SharedFormatString) {
                     var streamElement = new StreamArrayElementViewModel(history, (FieldArrayElementViewModel)viewModel, model, item.Name, itemAddress);
                     streamElement.DataChanged += ForwardModelChanged;
                     streamElement.DataMoved += ForwardModelDataMoved;
                     Children.Add(streamElement);
                  } else {
                     throw new NotImplementedException();
                  }
               }
            }
            itemAddress += item.Length;
         }
      }

      private void ForwardModelChanged(object sender, EventArgs e) => ModelDataChanged?.Invoke(this, model.GetNextRun(Address));
      private void ForwardModelDataMoved(object sender, (int originalStart, int newStart) e) => ModelDataMoved?.Invoke(this, e);
   }
}

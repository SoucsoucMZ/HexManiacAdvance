﻿using HavenSoft.Gen3Hex.Model;
using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace HavenSoft.Gen3Hex.ViewModel {
   public class Selection : INotifyPropertyChanged {

      private readonly StubCommand
         moveSelectionStart = new StubCommand(),
         moveSelectionEnd = new StubCommand();

      private readonly ScrollRegion scroll;

      private Point selectionStart, selectionEnd;

      public Point SelectionStart {
         get => selectionStart;
         set {
            if (selectionStart.Equals(value)) return;

            if (!scroll.ScrollToPoint(ref value)) {
               SelectionLeaving?.Invoke(this, selectionStart);
            }

            if (TryUpdate(ref selectionStart, value)) {
               SelectionEnd = selectionStart;
            }
         }
      }

      public Point SelectionEnd {
         get => selectionEnd;
         set {
            scroll.ScrollToPoint(ref value);

            TryUpdate(ref selectionEnd, value);
         }
      }

      public ICommand MoveSelectionStart => moveSelectionStart;

      public ICommand MoveSelectionEnd => moveSelectionEnd;

      public event PropertyChangedEventHandler PropertyChanged;

      public event EventHandler<Point> SelectionLeaving;

      public Selection(ScrollRegion scrollRegion) {
         scroll = scrollRegion;
         scroll.ScrollChanged += (sender, e) => ShiftSelectionFromScroll(e);

         moveSelectionStart.CanExecute = args => true;
         moveSelectionStart.Execute = args => MoveSelectionStartExecuted((Direction)args);
         moveSelectionEnd.CanExecute = args => true;
         moveSelectionEnd.Execute = args => MoveSelectionEndExecuted((Direction)args);
      }

      public bool IsSelected(Point point) {
         if (point.X < 0 || point.X >= scroll.Width) return false;

         var selectionStart = scroll.ViewPointToDataIndex(SelectionStart);
         var selectionEnd = scroll.ViewPointToDataIndex(SelectionEnd);
         var middle = scroll.ViewPointToDataIndex(point);

         var leftEdge = Math.Min(selectionStart, selectionEnd);
         var rightEdge = Math.Max(selectionStart, selectionEnd);

         return leftEdge <= middle && middle <= rightEdge;
      }

      /// <summary>
      /// Changing the scrollregion's width visibly moves the selection.
      /// But if we updated the selection using SelectionStart and SelectionEnd, it would auto-scroll.
      /// </summary>
      public void ChangeWidth(int newWidth) {
         var start = scroll.ViewPointToDataIndex(selectionStart);
         var end = scroll.ViewPointToDataIndex(selectionEnd);

         scroll.Width = newWidth;

         TryUpdate(ref selectionStart, scroll.DataIndexToViewPoint(start));
         TryUpdate(ref selectionEnd, scroll.DataIndexToViewPoint(end));

      }

      /// <summary>
      /// When the scrolling changes, the selection has to move as well.
      /// This is because the selection is in terms of the viewPort, not the overall data.
      /// Nothing in this method notifies because any amount of scrolling means we already need a complete redraw.
      /// </summary>
      private void ShiftSelectionFromScroll(int distance) {
         var start = scroll.ViewPointToDataIndex(selectionStart);
         var end = scroll.ViewPointToDataIndex(selectionEnd);

         start -= distance;
         end -= distance;

         selectionStart = scroll.DataIndexToViewPoint(start);
         selectionEnd = scroll.DataIndexToViewPoint(end);
      }

      private void MoveSelectionStartExecuted(Direction direction) {
         var dif = ScrollRegion.DirectionToDif[direction];
         SelectionStart = SelectionEnd + dif;
      }

      private void MoveSelectionEndExecuted(Direction direction) {
         var dif = ScrollRegion.DirectionToDif[direction];
         SelectionEnd += dif;
      }

      /// <summary>
      /// Utility function to make writing property updates easier.
      /// If the backing field's value does not match the new value, the backing field is updated and PropertyChanged gets called.
      /// </summary>
      /// <typeparam name="T">The type of the property being updated.</typeparam>
      /// <param name="backingField">A reference to the backing field of the property being changed.</param>
      /// <param name="newValue">The new value for the property.</param>
      /// <param name="propertyName">The name of the property to notify on. If the property is the caller, the compiler will figure this parameter out automatically.</param>
      /// <returns>false if the data did not need to be updated, true if it did.</returns>
      private bool TryUpdate<T>(ref T backingField, T newValue, [CallerMemberName]string propertyName = null) where T : IEquatable<T> {
         if (backingField == null && newValue == null) return false;
         if (backingField != null && backingField.Equals(newValue)) return false;
         backingField = newValue;
         PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
         return true;
      }
   }
}

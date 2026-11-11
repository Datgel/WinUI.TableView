using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.VisualStudio.TestTools.UnitTesting.AppContainer;
using System;
using System.Collections.Specialized;

namespace WinUI.TableView.Tests;

[TestClass]
public class TableViewColumnsCollectionTests
{
    [UITestMethod]
    public void Constructor_ShouldInitializeTableViewProperty()
    {
        var tableView = new TableView();
        var collection = new TableViewColumnsCollection(tableView);
        Assert.AreEqual(tableView, collection.TableView);
    }

    [UITestMethod]
    public void Add_ShouldRaiseCollectionChangedEvent()
    {
        var tableView = new TableView();
        var collection = new TableViewColumnsCollection(tableView);
        var column = new TableViewTextColumn();

        var eventRaised = false;
        collection.CollectionChanged += (s, e) => eventRaised = true;

        collection.Add(column);

        Assert.IsTrue(eventRaised);
    }

    [UITestMethod]
    public void Remove_ShouldRaiseCollectionChangedEvent()
    {
        var tableView = new TableView();
        var collection = new TableViewColumnsCollection(tableView);
        var column = new TableViewTextColumn();

        collection.Add(column);

        var eventRaised = false;
        collection.CollectionChanged += (s, e) => eventRaised = true;

        collection.Remove(column);

        Assert.IsTrue(eventRaised);
    }

    [UITestMethod]
    public void VisibleColumns_ShouldReturnOnlyVisibleColumns()
    {
        var tableView = new TableView();
        var collection = new TableViewColumnsCollection(tableView);

        var visibleColumn = new TableViewTextColumn { Visibility = Visibility.Visible };
        var hiddenColumn = new TableViewTextColumn { Visibility = Visibility.Collapsed };

        collection.Add(visibleColumn);
        collection.Add(hiddenColumn);

        var visibleColumns = collection.VisibleColumns;

        Assert.AreEqual(1, visibleColumns.Count);
        Assert.AreEqual(visibleColumn, visibleColumns[0]);
    }

    [UITestMethod]
    public void HandleColumnPropertyChanged_ShouldRaiseColumnPropertyChangedEvent()
    {
        var tableView = new TableView();
        var collection = new TableViewColumnsCollection(tableView);
        var column = new TableViewTextColumn();

        collection.Add(column);

        var eventRaised = false;
        collection.ColumnPropertyChanged += (s, e) => eventRaised = true;

        collection.HandleColumnPropertyChanged(column, "TestProperty");

        Assert.IsTrue(eventRaised);
    }

    [UITestMethod]
    public void HandleColumnPropertyChanged_ShouldNotRaiseEvent_ForInvalidColumn()
    {
        var tableView = new TableView();
        var collection = new TableViewColumnsCollection(tableView);
        var column = new TableViewTextColumn();

        var eventRaised = false;
        collection.ColumnPropertyChanged += (s, e) => eventRaised = true;

        collection.HandleColumnPropertyChanged(column, "TestProperty");

        Assert.IsFalse(eventRaised);
    }

    [UITestMethod]
    public void ResetCollection_ShouldRaiseCollectionChangedEvent()
    {
        var tableView = new TableView();
        var collection = new TableViewColumnsCollection(tableView);
        var column1 = new TableViewTextColumn();
        var column2 = new TableViewTextColumn();

        collection.Add(column1);
        collection.Add(column2);

        var eventRaised = false;
        collection.CollectionChanged += (s, e) =>
        {
            if (e.Action == NotifyCollectionChangedAction.Reset)
            {
                eventRaised = true;
            }
        };

        collection.Clear();

        Assert.IsTrue(eventRaised);
    }


    [UITestMethod]
    public void VisibleColumns_ShouldReturnColumnsInCorrectOrder()
    {
        var tableView = new TableView();
        var collection = new TableViewColumnsCollection(tableView);

        var column1 = new TableViewTextColumn { Header = "column1", Visibility = Visibility.Visible, Order = 1 };
        var column2 = new TableViewTextColumn { Header = "column2", Visibility = Visibility.Visible, Order = 2 };
        var column3 = new TableViewTextColumn { Header = "column3", Visibility = Visibility.Visible, Order = 1 };

        collection.Add(column1);
        collection.Add(column2);
        collection.Add(column3);

        var visibleColumns = collection.VisibleColumns;

        Assert.AreEqual(column1, visibleColumns[0]);
        Assert.AreEqual(column3, visibleColumns[1]);
        Assert.AreEqual(column2, visibleColumns[2]);
    }

    [UITestMethod]
    public void AddDuplicateColumns_ShouldHandleCorrectly()
    {
        var tableView = new TableView();
        var collection = new TableViewColumnsCollection(tableView);
        var column = new TableViewTextColumn();

        collection.Add(column);
        collection.Add(column);

        Assert.AreEqual(2, collection.Count);
    }

    [UITestMethod]
    public void ColumnVisibilityChange_ShouldUpdateVisibleColumns()
    {
        var tableView = new TableView();
        var collection = new TableViewColumnsCollection(tableView);

        var column = new TableViewTextColumn { Visibility = Visibility.Visible };
        collection.Add(column);

        Assert.AreEqual(1, collection.VisibleColumns.Count);

        column.Visibility = Visibility.Collapsed;

        Assert.AreEqual(0, collection.VisibleColumns.Count);
    }

    [UITestMethod]
    public void Add_ShouldThrowException_ForInvalidObjectType()
    {
        var tableView = new TableView();
        var collection = new TableViewColumnsCollection(tableView);

        var invalidObject = new TextBox();

        Assert.ThrowsExactly<InvalidCastException>(() => collection.Add(invalidObject));
    }

    [UITestMethod]
    public void UpdateFrozenColumns_ShouldFreezeLeadingVisibleColumnsOnly()
    {
        var tableView = new TableView { FrozenColumnCount = 2 };
        var collection = new TableViewColumnsCollection(tableView);

        var first = new TableViewTextColumn();
        var hidden = new TableViewTextColumn { Visibility = Visibility.Collapsed };
        var second = new TableViewTextColumn();
        var third = new TableViewTextColumn();

        collection.Add(first);
        collection.Add(hidden);
        collection.Add(second);
        collection.Add(third);

        // The first FrozenColumnCount VISIBLE columns are frozen; the hidden one does not count.
        Assert.IsTrue(first.IsFrozen);
        Assert.IsTrue(second.IsFrozen);
        Assert.IsFalse(third.IsFrozen);
    }

    [UITestMethod]
    public void UpdateFrozenColumns_ShouldHonourOrderOverInsertionOrder()
    {
        var tableView = new TableView { FrozenColumnCount = 1 };
        var collection = new TableViewColumnsCollection(tableView);

        var addedFirst = new TableViewTextColumn { Order = 2 };
        var addedSecond = new TableViewTextColumn { Order = 1 };

        collection.Add(addedFirst);
        collection.Add(addedSecond);

        Assert.IsFalse(addedFirst.IsFrozen);
        Assert.IsTrue(addedSecond.IsFrozen);
    }

    [UITestMethod]
    public void Add_ManyColumns_ShouldNotBeSuperLinear()
    {
        // Guards the fix for UpdateFrozenColumns evaluating VisibleColumns inside its loop on every
        // Add, which made building an n-column table O(n^3 log n).
        // A SCALE-FREE assertion, not a wall-clock budget: an absolute bound is a property of the
        // machine (an x64 Debug AppContainer took 794 ms for 350 columns on a dev box where the fixed
        // code is still fast in shape), so it fails on a slow agent and passes a slow regression on a
        // fast one. Doubling n costs ~2-3.5x when the cost is linear-ish and ~8x when it is cubic
        // (measured 7.98x unfixed, 2.6-3.5x fixed); 6 sits between 2^2 and 2^3, fitted to neither.
        static long Build(int n)
        {
            var collection = new TableViewColumnsCollection(new TableView());
            // Datgel DH-2231: collect BEFORE timing, so garbage left by earlier UI tests (tables they loaded and
            // released) is not collected inside one of the two timed builds - a single gen-2 pause there moved the
            // ratio from ~2.5x to ~7x on hosted CI with the column-build code unchanged.
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            for (var i = 0; i < n; i++)
            {
                collection.Add(new TableViewTextColumn { Header = $"Column {i}" });
            }
            stopwatch.Stop();
            Assert.AreEqual(n, collection.Count);
            return Math.Max(1, stopwatch.ElapsedMilliseconds);
        }

        Build(175); // warm the JIT so the first timed run is not charged for it
        var half = Build(175);
        var full = Build(350);
        var ratio = (double)full / half;

        Assert.IsTrue(ratio < 6,
            $"Doubling the column count from 175 ({half} ms) to 350 ({full} ms) cost {ratio:0.00}x; "
            + "a cubic column build costs ~8x.");
    }
}

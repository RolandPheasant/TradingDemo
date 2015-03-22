﻿using System;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using DynamicData;
using DynamicData.Binding;
using DynamicData.Controllers;
using DynamicData.ReactiveUI;
using ReactiveUI;
using Trader.Client.Infrastucture;
using Trader.Domain.Infrastucture;

namespace Trader.Client.Views
{
    public class LogEntryViewer : ReactiveObject, IDisposable
    {
        private readonly IDisposable _cleanUp;
        private readonly ILogEntryService _logEntryService;
        private readonly FilterController<LogEntryProxy> _filter = new FilterController<LogEntryProxy>(l => true);
        private readonly ReactiveList<LogEntryProxy> _data = new ReactiveList<LogEntryProxy>();
        private readonly SelectionController _selectionController = new SelectionController();
        private readonly ReactiveCommand<object> _deleteCommand;
        private LogEntrySummary _summary = LogEntrySummary.Empty;
        private string _searchText=string.Empty;
        private string _removeText;
        private readonly ObservableAsPropertyHelper<string> _deleteItemsText;


        public LogEntryViewer(ILogEntryService logEntryService)
        {
            _logEntryService = logEntryService;
 
            //apply filter when search text has changed
            var filterApplier = this.WhenAnyValue(x => x.SearchText)
                .Throttle(TimeSpan.FromMilliseconds(250))
                .Select(BuildFilter)
                .Subscribe(_filter.Change);


            //filter, sort and populate reactive list.
            var loader = logEntryService.Items.Connect()
                .Transform(le => new LogEntryProxy(le))
                .DelayRemove(TimeSpan.FromSeconds(0.75),proxy =>
                                                {
                                                    proxy.FlagForRemove();
                                                    _selectionController.DeSelect(proxy);
                                                })
                .Filter(_filter)
                .Sort(SortExpressionComparer<LogEntryProxy>.Descending(le=>le.TimeStamp).ThenByDescending(l => l.Key))
                .ObserveOn(RxApp.MainThreadScheduler)
                .Bind(_data)
                .DisposeMany()
                .Subscribe();

            //aggregate total items
            var summariser = logEntryService.Items.Connect()
                        .QueryWhenChanged(query =>
                        {
                            var items = query.Items.ToList();
                            var debug = items.Count(le => le.Level == LogLevel.Debug);
                            var info = items.Count(le => le.Level == LogLevel.Info);
                            var warn = items.Count(le => le.Level == LogLevel.Warning);
                            var error = items.Count(le => le.Level == LogLevel.Error);
                            return new LogEntrySummary(debug, info, warn, error);
                        })
                        .Subscribe(s => Summary = s);

            //manage user selection, delete items command
            var selectedItems = _selectionController.Selected.ToObservableChangeSet().Transform(obj => (LogEntryProxy)obj).Publish();
            
            //Build a message from selected items
            _deleteItemsText = selectedItems
                .QueryWhenChanged(query =>
                {
                    if (query.Count == 0) return "Select log entries to delete";
                    if (query.Count == 1) return "Delete selected log entry?";
                    return string.Format("Delete {0} log entries?", query.Count);
                })
                .StartWith("Select log entries to delete")
                .ToProperty(this, viewmodel => viewmodel.DeleteItemsText);

            //covert stream into an observable list so we can get a handle on items in thread safe manner. we could use the _data
            //but that is only thread safe if all the code is called from MainThread
            var selectedCache = selectedItems.AsObservableList();

            //make a command out of selected items - enabling the command when there is a selection 
            _deleteCommand = selectedItems
                                    .QueryWhenChanged(query => query.Count > 0)
                                    .ToCommand();

            //Assign action when the command is invoked
           var commandInvoker =  this.WhenAnyObservable(x => x.DeleteCommand)
                    .Subscribe(_ =>
                    {
                        var toRemove = selectedCache.Items.Select(proxy => proxy.Key).ToArray();
                        _logEntryService.Remove(toRemove);
                    });

            var connected = selectedItems.Connect();

            _cleanUp= Disposable.Create(() =>
            {
                loader.Dispose();
                filterApplier.Dispose();
                _filter.Dispose();
                connected.Dispose();
                _deleteItemsText.Dispose();
                selectedCache.Dispose();
                _selectionController.Dispose();
                commandInvoker.Dispose();
                summariser.Dispose();
            });
        }

        private Func<LogEntryProxy, bool> BuildFilter(string searchText)
        {
            if (string.IsNullOrEmpty(SearchText))
                return logentry => true;

            return logentry => logentry.Message.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
                            || logentry.Level.ToString().Contains(SearchText, StringComparison.OrdinalIgnoreCase);
        }

        public string SearchText
        {
            get { return _searchText; }
            set { this.RaiseAndSetIfChanged(ref _searchText, value); } 
        }

        public string DeleteItemsText
        {
            get { return _deleteItemsText.Value; }
        }



        public LogEntrySummary Summary
        {
            get { return _summary; }
            set { this.RaiseAndSetIfChanged(ref _summary, value); }
        }

        public ReactiveList<LogEntryProxy> Data
        {
            get { return _data; }
        }
        
        public ReactiveCommand<object> DeleteCommand
        {
            get { return _deleteCommand; }
        }

        public SelectionController Selector
        {
            get { return _selectionController; }
        }


        public void Dispose()
        {
           _cleanUp.Dispose();
        }
    }
}

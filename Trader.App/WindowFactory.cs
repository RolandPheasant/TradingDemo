﻿using System;
using Dragablz;
using Trader.Domain.Infrastucture;

namespace Trader.Client
{
    public class WindowFactory
    {
        private readonly IObjectProvider _objectProvider;

        public WindowFactory(IObjectProvider objectProvider)
        {
            _objectProvider = objectProvider;
        }

        public MainWindow Create(bool showMenu=false)
        {
            var window = new MainWindow();
            var model = _objectProvider.Get<WindowViewModel>();
            if (showMenu) model.ShowMenu();

            window.DataContext = model;

            window.Closing += (sender, e) =>
                              {
                                  if (TabablzControl.GetIsClosingAsPartOfDragOperation(window)) return;

                                  var todispose = ((MainWindow) sender).DataContext as IDisposable;
                                  if (todispose != null) todispose.Dispose();
                              };

            return window;
        }
    }
}
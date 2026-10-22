using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace TVLR_Selain_3;

public partial class MainWindow : Window
{
    private static readonly Func<DataGrid, Vector, bool>? DataGridUpdateScrollDelegate =
        (Func<DataGrid, Vector, bool>?)typeof(DataGrid).GetMethod("UpdateScroll", BindingFlags.NonPublic | BindingFlags.Instance)
            ?.CreateDelegate(typeof(Func<DataGrid, Vector, bool>));
    private Valilehti? _draggedTab;
    private Point _dragStartPos;
    private bool _isDraggingTab;

    private string _pvmPuskuri = "";
    private readonly DispatcherTimer _pvmTimer = new() { Interval = TimeSpan.FromMilliseconds(800) };
    
    private bool _isAutoScrolling;
    private Point _autoScrollOrigin;
    private long _autoScrollStartTime;
    private double _autoScrollSpeedY;
    private readonly DispatcherTimer _autoScrollTimer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private ScrollBar? _gridVerticalScrollBar;
    private IPointer? _capturedPointer;
    
    
    private static readonly double[] BaseColumnWidths = { 45, 105, 75, 85, 120, 44, -1, 105, 105, 320 };

    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainWindowViewModel();

        _pvmTimer.Tick += (_, __) =>
        {
            _pvmTimer.Stop();
            _pvmPuskuri = "";
        };

        _autoScrollTimer.Tick += AutoScrollTimer_Tick;
        
        OhjelmaGrid.AddHandler(InputElement.PointerWheelChangedEvent, OhjelmaGrid_PointerWheelChanged, RoutingStrategies.Tunnel);
        OhjelmaGrid.AddHandler(InputElement.PointerPressedEvent, OhjelmaGrid_PointerPressed, RoutingStrategies.Tunnel);

        AddHandler(InputElement.PointerMovedEvent, Window_PointerMoved, RoutingStrategies.Tunnel);
        AddHandler(InputElement.PointerReleasedEvent, Window_PointerReleased, RoutingStrategies.Tunnel);
        AddHandler(InputElement.PointerPressedEvent, Window_PointerPressed, RoutingStrategies.Tunnel);

        Deactivated += (_, __) =>
        {
            if (_isAutoScrolling) StopAutoScroll();
        };

        Loaded += async (_, __) =>
        {
            if (Avalonia.Controls.Design.IsDesignMode) return;

            if (SovellusAsetukset.Instance.AutoPaivitys)
            {
                await Task.Delay(5500);
                if (DataContext is MainWindowViewModel vm)
                {
                    await vm.TarkistaPaivityksetAsync(
                        vahvistaKysely: (viesti, otsikko) => ShowMessageBoxAsync(otsikko, viesti, isYesNo: true),
                        naytaViesti: (viesti, otsikko) => _ = ShowMessageBoxAsync(otsikko, viesti, isYesNo: false),
                        manuaalinen: false
                    );
                }
            }
        };
    }

    private void OhjelmaGrid_PointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            if (DataContext is MainWindowViewModel vm)
            {
                if (e.Delta.Y > 0)
                {
                    vm.SuurennaFonttikokoa();
                }
                else if (e.Delta.Y < 0)
                {
                    vm.PienennaFonttikokoa();
                }

                e.Handled = true;
            }
        }
    }

    private int GetHitColumnIndex(Visual? hitSource, Point clickPos)
    {
        var row = hitSource as DataGridRow ?? hitSource?.FindAncestorOfType<DataGridRow>();
        if (row != null)
        {
            double x = clickPos.X;
            double currentX = 0;
            for (int i = 0; i < OhjelmaGrid.Columns.Count; i++)
            {
                var col = OhjelmaGrid.Columns[i];
                if (!col.IsVisible) continue;
                currentX += col.ActualWidth;
                if (x <= currentX)
                {
                    return i;
                }
            }
        }
        return -1;
    }

    private async void OhjelmaGrid_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(OhjelmaGrid);

        // Hiiren keskinäppäin
        if (point.Properties.IsMiddleButtonPressed)
        {
            if (_isAutoScrolling)
            {
                StopAutoScroll();
            }
            else
            {
                StartAutoScroll(e.GetPosition(this), e.Pointer);
            }
            e.Handled = true;
            return;
        }

        if (_isAutoScrolling)
        {
            StopAutoScroll();
            e.Handled = true;
            return;
        }

        if (point.Properties.IsRightButtonPressed)
        {
            var hitVisual = e.Source as Visual;
            var rowVisual = hitVisual as DataGridRow ?? hitVisual?.FindAncestorOfType<DataGridRow>();
            var row = (rowVisual?.DataContext as TvlrRow) ?? (OhjelmaGrid.SelectedItem as TvlrRow);

            if (row != null)
            {
                int colIndex = -1;
                if (rowVisual != null)
                {
                    var pos = e.GetPosition(rowVisual);
                    colIndex = GetHitColumnIndex(rowVisual, pos);
                }

                if (colIndex < 0 && OhjelmaGrid.CurrentColumn != null)
                {
                    colIndex = OhjelmaGrid.Columns.IndexOf(OhjelmaGrid.CurrentColumn);
                }

                if (colIndex >= 0 && colIndex < OhjelmaGrid.Columns.Count)
                {
                    var col = OhjelmaGrid.Columns[colIndex];
                    string header = col.Header?.ToString() ?? "";
                    string textToCopy = "";
                    if (header == "Päivä") textToCopy = row.PaivaStr;
                    else if (header == "Kello") textToCopy = row.KelloStr;
                    else if (header == "Kesto") textToCopy = row.KestoStr;
                    else if (header == "Kanava") textToCopy = row.VerkkoNimi;
                    else if (header == "Nimi") textToCopy = row.Nimi;
                    else if (header == "Tekstitys") textToCopy = row.TEKS;
                    else if (header == "Selostus") textToCopy = row.SELO;
                    else if (header == "Toimitus") textToCopy = row.TEKI;
                    else if (header == "📄") textToCopy = row.TIETOJA;
                    else if (header == "") textToCopy = row.VkopvStr;

                    if (!string.IsNullOrEmpty(textToCopy))
                    {
                        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
                        if (clipboard != null)
                        {
                            await clipboard.SetTextAsync(textToCopy);
                            if (DataContext is MainWindowViewModel vm)
                            {
                                vm.StatusTeksti = $"Kopioitu leikepöydälle: {textToCopy}";
                            }
                        }
                    }
                }
            }
        }
    }

    private void Window_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(this);

        // Hiiren keskinäppäin
        if (point.Properties.IsMiddleButtonPressed)
        {
            if (_isAutoScrolling)
            {
                StopAutoScroll();
            }
            else
            {
                StartAutoScroll(e.GetPosition(this), e.Pointer);
            }
            e.Handled = true;
            return;
        }

        if (_isAutoScrolling)
        {
            StopAutoScroll();
            e.Handled = true;
        }
    }

    private void Window_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_isAutoScrolling && e.InitialPressMouseButton == MouseButton.Middle)
        {
            if (Environment.TickCount64 - _autoScrollStartTime > 250)
            {
                StopAutoScroll();
                e.Handled = true;
            }
        }
    }

    private void Window_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (_isAutoScrolling)
        {
            var currentPos = e.GetPosition(this);
            double dy = currentPos.Y - _autoScrollOrigin.Y;
            const double deadzone = 8.0;

            if (Math.Abs(dy) <= deadzone)
            {
                _autoScrollSpeedY = 0;
            }
            else
            {
                double dist = dy > 0 ? (dy - deadzone) : (dy + deadzone);
                double absDist = Math.Abs(dist);
                double speed = (absDist * 0.15) + Math.Pow(absDist / 18.0, 1.45) * 1.6;
                double clampedSpeed = Math.Clamp(speed, 0.5, 160.0);
                _autoScrollSpeedY = Math.Sign(dist) * clampedSpeed;
            }
        }
    }

    private void StartAutoScroll(Point pos, IPointer? pointer)
    {
        _isAutoScrolling = true;
        _autoScrollOrigin = pos;
        _autoScrollStartTime = Environment.TickCount64;
        _autoScrollSpeedY = 0;

        _capturedPointer = pointer;
        try { _capturedPointer?.Capture(this); } catch { }

        _gridVerticalScrollBar ??= OhjelmaGrid.GetVisualDescendants()
            .OfType<ScrollBar>()
            .FirstOrDefault(sb => sb.Name == "PART_VerticalScrollbar" || sb.Orientation == Orientation.Vertical);

        Canvas.SetLeft(AutoScrollIndicator, pos.X - 16);
        Canvas.SetTop(AutoScrollIndicator, pos.Y - 16);
        AutoScrollIndicator.IsVisible = true;

        Cursor = new Cursor(StandardCursorType.SizeNorthSouth);
        _autoScrollTimer.Start();
    }

    private void StopAutoScroll()
    {
        if (!_isAutoScrolling) return;
        _isAutoScrolling = false;
        _autoScrollTimer.Stop();
        _autoScrollSpeedY = 0;

        try { _capturedPointer?.Capture(null); } catch { }
        _capturedPointer = null;

        AutoScrollIndicator.IsVisible = false;
        Cursor = Cursor.Default;
    }

    private void AutoScrollTimer_Tick(object? sender, EventArgs e)
    {
        if (!_isAutoScrolling)
        {
            _autoScrollTimer.Stop();
            return;
        }

        if (Math.Abs(_autoScrollSpeedY) < 0.05) return;

        if (DataGridUpdateScrollDelegate != null && OhjelmaGrid != null)
        {
            DataGridUpdateScrollDelegate(OhjelmaGrid, new Vector(0, -_autoScrollSpeedY));
        }
        else if (OhjelmaGrid != null)
        {
            _gridVerticalScrollBar ??= OhjelmaGrid.GetVisualDescendants()
                .OfType<ScrollBar>()
                .FirstOrDefault(sb => sb.Name == "PART_VerticalScrollbar" || sb.Orientation == Orientation.Vertical);

            if (_gridVerticalScrollBar != null && _gridVerticalScrollBar.Maximum > _gridVerticalScrollBar.Minimum)
            {
                double currentVal = _gridVerticalScrollBar.Value;
                double targetVal = Math.Clamp(currentVal + _autoScrollSpeedY, _gridVerticalScrollBar.Minimum, _gridVerticalScrollBar.Maximum);
                if (Math.Abs(targetVal - currentVal) > 0.01)
                {
                    _gridVerticalScrollBar.Value = targetVal;
                }
            }
        }
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (DataContext is MainWindowViewModel vm)
        {
            vm.PropertyChanged += (_, ev) =>
            {
                if (ev.PropertyName == nameof(MainWindowViewModel.HakutuloksetFonttikoko))
                {
                    PaivitaSarakkeidenLeveydet(vm.HakutuloksetFonttikoko);
                }
            };

            vm.PyydaSkrollaus += (row) =>
            {
                Dispatcher.UIThread.Post(async () =>
                {
                    await Task.Delay(80);
                    if (OhjelmaGrid != null)
                    {
                        OhjelmaGrid.SelectedItem = row;
                        OhjelmaGrid.ScrollIntoView(row, OhjelmaGrid.Columns[0]);
                    }
                }, DispatcherPriority.Loaded);
            };
        }
    }

    private void PaivitaSarakkeidenLeveydet(double fontSize)
    {
        if (OhjelmaGrid == null || OhjelmaGrid.Columns.Count < BaseColumnWidths.Length) return;

        double ratio = fontSize / 13.0;

        for (int i = 0; i < BaseColumnWidths.Length; i++)
        {
            double baseWidth = BaseColumnWidths[i];
            if (baseWidth < 0)
            {
                OhjelmaGrid.Columns[i].Width = new DataGridLength(1, DataGridLengthUnitType.Star);
            }
            else
            {
                double w = Math.Round(baseWidth * ratio);
                if (i == 5) w = Math.Max(38, w);
                OhjelmaGrid.Columns[i].Width = new DataGridLength(w, DataGridLengthUnitType.Pixel);
            }
        }
    }


    // VÄLILEHTIEN RAAHAUS
    private void TabCloseButton_Click(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is Button { CommandParameter: Valilehti tab } && DataContext is MainWindowViewModel vm)
        {
            vm.SuljeValilehti(tab);
        }
    }

    private void Tab_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        Visual? src = e.Source as Visual;
        while (src != null && src != sender)
        {
            if (src is Button)
            {
                e.Handled = true;
                return;
            }
            src = src.GetVisualParent();
        }

        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            if (sender is Control c && c.DataContext is Valilehti tab)
            {
                _draggedTab = tab;
                _dragStartPos = e.GetPosition(this);
                _isDraggingTab = false;
            }
        }
    }

    private void Tab_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (_draggedTab == null) return;

        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            _draggedTab = null;
            _isDraggingTab = false;
            return;
        }

        var currentPos = e.GetPosition(this);
        if (!_isDraggingTab)
        {
            if (Math.Abs(currentPos.X - _dragStartPos.X) > 10)
            {
                _isDraggingTab = true;
            }
        }

        if (_isDraggingTab)
        {
            var hit = this.InputHitTest(currentPos) as Visual;
            Valilehti? targetTab = null;

            Visual? curr = hit;
            while (curr != null)
            {
                if (curr is Control { DataContext: Valilehti v } && v != _draggedTab)
                {
                    targetTab = v;
                    break;
                }
                curr = curr.GetVisualParent();
            }

            if (targetTab != null)
            {
                if (DataContext is MainWindowViewModel vm)
                {
                    int oldIdx = vm.Valilehdet.IndexOf(_draggedTab);
                    int newIdx = vm.Valilehdet.IndexOf(targetTab);

                    if (oldIdx >= 0 && newIdx >= 0 && oldIdx != newIdx)
                    {
                        vm.Valilehdet.Move(oldIdx, newIdx);
                        vm.PaivitaValilehtienNumerot();
                        _dragStartPos = currentPos;
                    }
                }
            }
        }
    }

    private void Tab_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _draggedTab = null;
        _isDraggingTab = false;
    }
    
    // DATAGRID JA APUTOIMINNOT
    private void OhjelmaGrid_LoadingRow(object? sender, DataGridRowEventArgs e)
    {
        if (e.Row.DataContext is TvlrRow r && r.OnkoKorostettu)
        {
            if (!e.Row.Classes.Contains("highlighted-row"))
                e.Row.Classes.Add("highlighted-row");
        }
        else
        {
            e.Row.Classes.Remove("highlighted-row");
        }
    }

    private void OhjelmaGrid_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is DataGrid grid)
        {
            var rowVisual = (e.Source as Visual) as DataGridRow ?? (e.Source as Visual)?.FindAncestorOfType<DataGridRow>();
            var row = (rowVisual?.DataContext as TvlrRow) ?? (grid.SelectedItem as TvlrRow);

            if (row != null)
            {
                int colIndex = -1;
                if (rowVisual != null)
                {
                    var pos = e.GetPosition(rowVisual);
                    colIndex = GetHitColumnIndex(rowVisual, pos);
                }

                if (colIndex < 0 && grid.CurrentColumn != null)
                {
                    colIndex = grid.Columns.IndexOf(grid.CurrentColumn);
                }
                
                if (colIndex >= 0 && colIndex <= 4 && DataContext is MainWindowViewModel vm)
                {
                    vm.AsetaKanavaJaPaiva(row.VerkkoNimi, row.Paiva.Date, row.DOCN);
                }
                else
                {
                    AvaaKuvaus(row);
                }
            }
        }
    }

    private void OhjelmaGrid_Sorting(object? sender, DataGridColumnEventArgs e)
    {
        e.Handled = true; // Estetään Avalonian oletuslajittelu
        if (DataContext is MainWindowViewModel vm)
        {
            string header = e.Column.Header?.ToString() ?? "";
            if (header.Equals("Päivä", StringComparison.OrdinalIgnoreCase))
            {
                vm.LajittelePaivanMukaan();
            }
            else if (header.Equals("Kesto", StringComparison.OrdinalIgnoreCase))
            {
                vm.LajitteleKestonMukaan();
            }
        }
    }

    private async void OhjelmaGrid_KeyDown(object? sender, KeyEventArgs e)
    {
        // Ctrl+C
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.C)
        {
            if (sender is DataGrid grid && grid.SelectedItem is TvlrRow row)
            {
                var col = grid.CurrentColumn;
                string header = col?.Header?.ToString() ?? "";
                string textToCopy = header switch
                {
                    "Päivä" => row.PaivaStr,
                    "Kello" => row.KelloStr,
                    "Kesto" => row.KestoStr,
                    "Kanava" => row.VerkkoNimi,
                    "Tekstitys" => row.TEKS,
                    "Selostus" => row.SELO,
                    "Toimitus" => row.TEKI,
                    "📄" => row.TIETOJA,
                    "" => row.VkopvStr,
                    _ => row.Nimi
                };
                if (string.IsNullOrEmpty(textToCopy))
                    textToCopy = row.Nimi;

                var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
                if (clipboard != null)
                {
                    await clipboard.SetTextAsync(textToCopy);
                    if (DataContext is MainWindowViewModel vm)
                    {
                        vm.StatusTeksti = $"Kopioitu leikepöydälle: {textToCopy}";
                    }
                }
                e.Handled = true;
            }
            return;
        }

        if (e.Key == Key.Space)
        {
            if (sender is DataGrid grid && grid.SelectedItem is TvlrRow row)
            {
                AvaaKuvaus(row);
                e.Handled = true;
            }
            return;
        }
        
        if ((e.Key >= Key.D0 && e.Key <= Key.D9) || (e.Key >= Key.NumPad0 && e.Key <= Key.NumPad9) || e.Key == Key.OemPeriod || e.Key == Key.Decimal)
        {
            char merkki;
            if (e.Key == Key.OemPeriod || e.Key == Key.Decimal) merkki = '.';
            else if (e.Key >= Key.D0 && e.Key <= Key.D9) merkki = (char)('0' + (e.Key - Key.D0));
            else merkki = (char)('0' + (e.Key - Key.NumPad0));

            _pvmTimer.Stop();
            _pvmTimer.Start();
            _pvmPuskuri += merkki;

            if (DateTime.TryParseExact(_pvmPuskuri, "d.M.yyyy", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out DateTime pvm))
            {
                if (DataContext is MainWindowViewModel vm)
                {
                    vm.HaeValilla = false;
                    vm.HaePaivalla = true;
                    vm.TarkkaPaiva = pvm;
                    _pvmPuskuri = "";
                    _pvmTimer.Stop();
                }
            }

            e.Handled = true;
        }
    }

    private void AvaaKuvaus(TvlrRow row)
    {
        string kuvaus = string.IsNullOrWhiteSpace(row.TIETOJA)
            ? "Tälle ohjelmalle ei ole kuvausta."
            : row.TIETOJA.Trim();

        ShowDescriptionPopup(row.Nimi, kuvaus);
    }

    private void ShowDescriptionPopup(string title, string body)
    {
        var popup = new Window
        {
            Title = title,
            SizeToContent = SizeToContent.WidthAndHeight,
            MinWidth = 320,
            MaxWidth = 640,
            MinHeight = 80,
            MaxHeight = 650,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Topmost = true,
            ShowInTaskbar = false,
            CanResize = true
        };

        var txt = new TextBox
        {
            Text = body,
            IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap,
            AcceptsReturn = true,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            Margin = new Thickness(12),
            MaxWidth = 600,
            FontSize = 14
        };

        var scroller = new ScrollViewer
        {
            Content = txt,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };

        popup.Content = scroller;

        popup.KeyDown += (s, e) =>
        {
            if (e.Key == Key.Escape) popup.Close();
        };

        popup.Deactivated += (s, e) => popup.Close();
        popup.Show(this);
    }

    private async void Asetukset_Click(object? sender, RoutedEventArgs e)
    {
        var asetukset = new AsetuksetWindow();
        await asetukset.ShowDialog(this);
    }

    private void TyhjennaHaku_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.Haku = "";
            txtHaku.Focus();
        }
    }

    private async void TarkistaPaivitykset_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            await vm.TarkistaPaivityksetAsync(
                vahvistaKysely: (viesti, otsikko) => ShowMessageBoxAsync(otsikko, viesti, isYesNo: true),
                naytaViesti: (viesti, otsikko) => _ = ShowMessageBoxAsync(otsikko, viesti, isYesNo: false),
                manuaalinen: true
            );
        }
    }

    private async Task<bool> ShowMessageBoxAsync(string title, string message, bool isYesNo = false)
    {
        var tcs = new TaskCompletionSource<bool>();

        var dialog = new Window
        {
            Title = title,
            Width = 460,
            Height = 280,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Topmost = true
        };

        var panel = new StackPanel { Margin = new Thickness(18), Spacing = 16 };
        var scroll = new ScrollViewer
        {
            MaxHeight = 160,
            Content = new TextBlock
            {
                Text = message,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 13
            }
        };
        panel.Children.Add(scroll);

        var btnPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 10,
            Margin = new Thickness(0, 10, 0, 0)
        };

        if (isYesNo)
        {
            var btnYes = new Button { Content = "Kyllä", Width = 80, HorizontalContentAlignment = HorizontalAlignment.Center };
            var btnNo = new Button { Content = "Ei", Width = 80, HorizontalContentAlignment = HorizontalAlignment.Center };

            btnYes.Click += (_, __) => { tcs.TrySetResult(true); dialog.Close(); };
            btnNo.Click += (_, __) => { tcs.TrySetResult(false); dialog.Close(); };

            btnPanel.Children.Add(btnYes);
            btnPanel.Children.Add(btnNo);
        }
        else
        {
            var btnOk = new Button { Content = "OK", Width = 80, HorizontalContentAlignment = HorizontalAlignment.Center };
            btnOk.Click += (_, __) => { tcs.TrySetResult(true); dialog.Close(); };
            btnPanel.Children.Add(btnOk);
        }

        panel.Children.Add(btnPanel);
        dialog.Content = panel;

        dialog.Closed += (_, __) => tcs.TrySetResult(false);
        await dialog.ShowDialog(this);

        return await tcs.Task;
    }

    private async Task ShowHighlightDialogAsync()
    {
        if (DataContext is not MainWindowViewModel vm) return;

        var dialog = new Window
        {
            Title = "Etsi näkyvistä ohjelmista",
            Width = 420,
            Height = 170,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Topmost = true
        };

        var panel = new StackPanel { Margin = new Thickness(16), Spacing = 12 };
        panel.Children.Add(new TextBlock { Text = "Korostettava teksti:", FontSize = 13 });

        var input = new TextBox { Text = vm.KorostusHaku };
        panel.Children.Add(input);

        var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
        var btnOk = new Button { Content = "Korosta", Width = 80, HorizontalContentAlignment = HorizontalAlignment.Center };
        var btnCancel = new Button { Content = "Peruuta", Width = 80, HorizontalContentAlignment = HorizontalAlignment.Center };

        btnOk.Click += (_, __) =>
        {
            vm.AsetaKorostusHaku(input.Text ?? "");
            dialog.Close();
        };

        btnCancel.Click += (_, __) => dialog.Close();

        input.KeyDown += (_, ke) =>
        {
            if (ke.Key == Key.Enter)
            {
                vm.AsetaKorostusHaku(input.Text ?? "");
                dialog.Close();
            }
            else if (ke.Key == Key.Escape)
            {
                dialog.Close();
            }
        };

        btnPanel.Children.Add(btnOk);
        btnPanel.Children.Add(btnCancel);
        panel.Children.Add(btnPanel);
        dialog.Content = panel;

        await dialog.ShowDialog(this);
    }
    
    // PIKANÄPPÄIMET
    protected override async void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e.Key == Key.Escape && _isAutoScrolling)
        {
            StopAutoScroll();
            e.Handled = true;
            return;
        }


        if (DataContext is MainWindowViewModel vm)
        {
            // Ctrl+Alt+F: Korostushaku
            if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.KeyModifiers.HasFlag(KeyModifiers.Alt) && e.Key == Key.F)
            {
                e.Handled = true;
                await ShowHighlightDialogAsync();
                return;
            }

            // Ctrl+F: Hakukenttään
            if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.F)
            {
                e.Handled = true;
                txtHaku.Focus();
                txtHaku.SelectAll();
                return;
            }

            // Ctrl+K: Kanavat
            if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.K)
            {
                e.Handled = true;
                btnKanavat.Flyout?.ShowAt(btnKanavat);
                return;
            }

            // Ctrl+P: Hae päivämäärällä
            if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.P)
            {
                e.Handled = true;
                vm.HaePaivalla = !vm.HaePaivalla;
                if (vm.HaePaivalla)
                {
                    vm.HaeValilla = false;
                    dtpPaiva.Focus();
                }
                return;
            }

            // Ctrl+A: Hae välillä
            if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.A && !txtHaku.IsFocused)
            {
                e.Handled = true;
                vm.HaeValilla = !vm.HaeValilla;
                if (vm.HaeValilla)
                {
                    vm.HaePaivalla = false;
                    dtpAlku.Focus();
                }
                return;
            }

            // Ctrl+Delete: Tyhjennä hakukenttä
            if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.Delete)
            {
                e.Handled = true;
                vm.Haku = "";
                return;
            }

            // Ctrl+Backspace: Tyhjennä kaikki suodattimet
            if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.Back)
            {
                e.Handled = true;
                vm.TyhjennaCommand.Execute(null);
                return;
            }

            // Ctrl+T: Uusi välilehti
            if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.T)
            {
                e.Handled = true;
                vm.LisaaValilehti();
                return;
            }

            // Ctrl+W: Sulje välilehti
            if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.W)
            {
                e.Handled = true;
                vm.SuljeValilehti(vm.AktiivinenValilehti);
                return;
            }

            // Ctrl+Tab / Ctrl+Shift+Tab: Välilehtien vaihto
            if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.Tab)
            {
                e.Handled = true;
                if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
                    vm.EdellinenValilehti();
                else
                    vm.SeuraavaValilehti();
                return;
            }

            // Ctrl+1..9 Siirry suoraan välilehteen
            if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
            {
                int tabIdx = -1;
                if (e.Key >= Key.D1 && e.Key <= Key.D9) tabIdx = (int)e.Key - (int)Key.D1;
                else if (e.Key >= Key.NumPad1 && e.Key <= Key.NumPad9) tabIdx = (int)e.Key - (int)Key.NumPad1;

                if (tabIdx >= 0)
                {
                    e.Handled = true;
                    vm.ValitseValilehti(tabIdx);
                    return;
                }
            }
        }

        // F1-F12: Kopiointipikanäppäimet
        if (e.Key >= Key.F1 && e.Key <= Key.F12)
        {
            if (SovellusAsetukset.Pikanappaimet.TryGetValue(e.Key, out string? malline) &&
                !string.IsNullOrWhiteSpace(malline))
            {
                var valitut = OhjelmaGrid.SelectedItems;
                if (valitut != null && valitut.Count > 0)
                {
                    var lines = new List<string>();
                    foreach (var item in valitut)
                    {
                        if (item is TvlrRow row)
                        {
                            string teksti = malline
                                .Replace("{NIMI}", row.Nimi)
                                .Replace("{PVM}", row.PaivaStr)
                                .Replace("{KANAVA}", row.VerkkoNimi)
                                .Replace("{KELLO}", row.KelloStr)
                                .Replace("{KESTO}", row.KestoStr)
                                .Replace("{TOIMITUS}", row.TEKI);
                            lines.Add(teksti);
                        }
                    }

                    if (lines.Count > 0)
                    {
                        IClipboard? clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
                        if (clipboard != null)
                        {
                            await clipboard.SetTextAsync(string.Join(Environment.NewLine, lines));

                            if (DataContext is MainWindowViewModel vm2)
                            {
                                vm2.StatusTeksti = lines.Count == 1
                                    ? $"Kopioitu leikepöydälle: {lines[0]}"
                                    : $"Kopioitu {lines.Count} ohjelmaa leikepöydälle.";
                            }
                        }

                        e.Handled = true;
                    }
                }
            }
        }
    }
}
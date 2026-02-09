using Microsoft.Win32;
using System;
using System.ComponentModel;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;
using System.Xml;

namespace WPF
{
    public partial class MainWindow : Window
    {
        private readonly ChatHandler _chatHandler;
        private Storyboard _dotsAnimation;

        private bool _hasConvertedForCurrentInput = false;
        private string? _lastSourceHash = null;

        public MainWindow()
        {
            
try
    {
        InitializeComponent();
    }
    catch (Exception ex)
    {
        MessageBox.Show(
            $"Błąd podczas inicjalizacji XAML:\n{ex}",
            "Fatalny błąd XAML",
            MessageBoxButton.OK,
            MessageBoxImage.Error);

        throw; // ważne — inaczej aplikacja może wisieć bez okna
    }

            // InitializeComponent();
            _chatHandler = new ChatHandler();

            TryLoadPascalHighlighting();

            // Animacja kropek - upewnij się, że JumpingDotsAnimation istnieje w Resources w XAML
            var animationResource = FindResource("JumpingDotsAnimation") as Storyboard;
            if (animationResource != null)
            {
                _dotsAnimation = animationResource.Clone();
            }

            UpdateConvertButtonStateAndLabel();
            SaveButton.IsEnabled = false;

            // --- MOTYW DOMYŚLNY ---
            ThemeToggle.IsChecked = false;           // false = Light
            ThemeService.ApplyTheme(AppTheme.Light);
            ThemeService.ThemeChanged += (_, theme) => ApplyEditorTheme(theme);
        }

        private void TryLoadPascalHighlighting()
        {
            try
            {
                var xshdPath = System.IO.Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory, "Syntax", "Pascal.xshd");

                if (File.Exists(xshdPath))
                {
                    using var reader = XmlReader.Create(xshdPath);
                    var xshd = HighlightingLoader.LoadXshd(reader);
                    var highlighting = HighlightingLoader.Load(xshd, HighlightingManager.Instance);
                    HighlightingManager.Instance.RegisterHighlighting("Pascal", new string[] { ".pas", ".ps" }, highlighting);
                    PascalCodeTextBox.SyntaxHighlighting = highlighting;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Błąd ładowania składni: {ex.Message}");
            }
        }

        // ----------- MOTYWY: obsługa przełącznika -----------

        private void ThemeToggle_Checked(object sender, RoutedEventArgs e)
        {
            ThemeService.ApplyTheme(AppTheme.Dark);
        }

        private void ThemeToggle_Unchecked(object sender, RoutedEventArgs e)
        {
            ThemeService.ApplyTheme(AppTheme.Light);
        }

        // ----------- MOTYWY: dostosowanie AvalonEdit -----------

        private void ApplyEditorTheme(AppTheme theme)
        {
            if (theme == AppTheme.Dark)
            {
                var bg = (Brush)FindResource("SurfaceVariantBrush");
                var fg = (Brush)FindResource("OnSurfaceBrush");
                var sel = new SolidColorBrush(Color.FromArgb(60, 144, 164, 255));
                var lineNum = (Brush)FindResource("OnSurfaceVariantBrush");

                ApplyToEditor(PascalCodeTextBox, bg, fg, sel, lineNum);
                ApplyToEditor(CSharpCodeTextBox, bg, fg, sel, lineNum);
            }
            else
            {
                var bg = (Brush)FindResource("SurfaceBrush");
                var fg = (Brush)FindResource("OnSurfaceBrush");
                var sel = new SolidColorBrush(Color.FromArgb(50, 30, 64, 244));
                var lineNum = (Brush)FindResource("OnSurfaceVariantBrush");

                ApplyToEditor(PascalCodeTextBox, bg, fg, sel, lineNum);
                ApplyToEditor(CSharpCodeTextBox, bg, fg, sel, lineNum);
            }
        }

        private static void ApplyToEditor(ICSharpCode.AvalonEdit.TextEditor editor,
                                          Brush background, Brush foreground, Brush selection, Brush lineNumbers)
        {
            if (editor == null)
                return;

            editor.Background = background;
            editor.Foreground = foreground;
            editor.LineNumbersForeground = lineNumbers;

            if (editor.TextArea != null)
            {
                // Ustawienie koloru zaznaczenia
                editor.TextArea.SelectionBrush = selection;

                // FIX: Jeśli CaretBrush nie istnieje, używamy bezpośrednio Caret.Brush
                if (editor.TextArea.Caret != null)
                {
                    editor.TextArea.Caret.CaretBrush = foreground;
                }
            }
        }

        // ======================================================
        //   Zamknięcie okna
        // ======================================================
        protected override void OnClosing(CancelEventArgs e)
        {
            _chatHandler.Cancel();

            try { _dotsAnimation?.Remove(LoadingDotsPanel); } catch { }

            if (LoadingDotsPanel != null)
                LoadingDotsPanel.Visibility = Visibility.Collapsed;

            try
            {
                PascalCodeTextBox.SyntaxHighlighting = null;
                CSharpCodeTextBox.SyntaxHighlighting = null;

                PascalCodeTextBox.Document?.UndoStack.ClearAll();
                CSharpCodeTextBox.Document?.UndoStack.ClearAll();
            }
            catch { }

            base.OnClosing(e);
        }

        protected override void OnClosed(EventArgs e)
        {
            _chatHandler.Dispose();
            base.OnClosed(e);
        }

        // ======================================================
        //   HASH
        // ======================================================
        private static string ComputeHash(string text)
        {
            using var sha256 = SHA256.Create();
            var bytes = Encoding.UTF8.GetBytes(text ?? string.Empty);
            var hash = sha256.ComputeHash(bytes);
            return Convert.ToBase64String(hash);
        }

        // ======================================================
        //   Aktualizacja przycisku Konwertuj/Odśwież
        // ======================================================
        private void UpdateConvertButtonStateAndLabel()
        {
            var hasText = !string.IsNullOrWhiteSpace(PascalCodeTextBox.Text);

            if (!hasText)
            {
                ConvertButton.IsEnabled = false;
                ConvertButton.Content = "Konwertuj";
                _hasConvertedForCurrentInput = false;
                _lastSourceHash = null;

                SaveButton.IsEnabled = !string.IsNullOrWhiteSpace(CSharpCodeTextBox.Text);
                return;
            }

            ConvertButton.IsEnabled = true;

            var currentHash = ComputeHash(PascalCodeTextBox.Text);
            if (_hasConvertedForCurrentInput &&
                string.Equals(currentHash, _lastSourceHash, StringComparison.Ordinal))
            {
                ConvertButton.Content = "Odśwież";
            }
            else
            {
                ConvertButton.Content = "Konwertuj";
            }

            SaveButton.IsEnabled = !string.IsNullOrWhiteSpace(CSharpCodeTextBox.Text);
        }

        // ======================================================
        //   Zmiana w edytorze Pascala
        // ======================================================
        private void PascalCodeTextBox_TextChanged(object? sender, EventArgs e)
        {
            var hasText = !string.IsNullOrWhiteSpace(PascalCodeTextBox.Text);

            if (!hasText)
            {
                _hasConvertedForCurrentInput = false;
                _lastSourceHash = null;
            }
            else
            {
                var currentHash = ComputeHash(PascalCodeTextBox.Text);
                if (!string.Equals(currentHash, _lastSourceHash, StringComparison.Ordinal))
                    _hasConvertedForCurrentInput = false;
            }

            UpdateConvertButtonStateAndLabel();
        }

        // ======================================================
        //   Konwertowanie
        // ======================================================
        private async void ConvertButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(PascalCodeTextBox.Text))
                return;

            var sourceText = PascalCodeTextBox.Text;
            var sourceHash = ComputeHash(sourceText);

            StartLoading();

            await Task.Yield();
            CSharpCodeTextBox.Visibility = Visibility.Collapsed;

            try
            {
                var convertedContent = await _chatHandler.FullPascalToValidatedCSharpAsync(sourceText);
                CSharpCodeTextBox.Text = convertedContent;
                CSharpCodeTextBox.Visibility = Visibility.Visible;

                _hasConvertedForCurrentInput = true;
                _lastSourceHash = sourceHash;

                SaveButton.IsEnabled = !string.IsNullOrWhiteSpace(CSharpCodeTextBox.Text);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error converting file: {ex.Message}",
                    "Błąd", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                StopLoading();
                UpdateConvertButtonStateAndLabel();
            }
        }

        // ======================================================
        //   Animacja kropek — helpery
        // ======================================================
        private void StartLoading()
        {
            ConvertButton.Visibility = Visibility.Collapsed;
            LoadingDotsPanel.Visibility = Visibility.Visible;
            _dotsAnimation?.Begin(LoadingDotsPanel, true);
        }

        private void StopLoading()
        {
            _dotsAnimation?.Stop(LoadingDotsPanel);
            LoadingDotsPanel.Visibility = Visibility.Collapsed;
            ConvertButton.Visibility = Visibility.Visible;
        }

        // ======================================================
        //   Wybór pliku
        // ======================================================
        private async void SelectFileButton_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                Filter = "Pliki Pascala (*.ps;*.pas;*.txt)|*.ps;*.pas;*.txt|Wszystkie pliki (*.*)|*.*"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                FileNameTextBlock.Text = openFileDialog.SafeFileName;
                var fileContent = await File.ReadAllTextAsync(openFileDialog.FileName);

                PascalCodeTextBox.Text = fileContent;
                PascalCodeTextBox.Visibility = Visibility.Visible;

                _hasConvertedForCurrentInput = false;
                _lastSourceHash = null;

                SaveButton.IsEnabled = false;

                UpdateConvertButtonStateAndLabel();
            }
        }

        // ======================================================
        //   Zapisz jako
        // ======================================================
        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(CSharpCodeTextBox.Text))
            {
                MessageBox.Show("Brak kodu do zapisania.",
                    "Uwaga", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var dialog = new SaveFileDialog
            {
                Filter = "Plik C# (*.cs)|*.cs",
                FileName = SuggestFileName()
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    File.WriteAllText(dialog.FileName,
                        CSharpCodeTextBox.Text,
                        new UTF8Encoding(false));

                    MessageBox.Show("Plik zapisano pomyślnie.",
                        "Sukces", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Nie udało się zapisać pliku:\n{ex.Message}",
                        "Błąd zapisu", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        // ======================================================
        //   Sugerowana nazwa pliku
        // ======================================================
        private string SuggestFileName()
        {
            var baseName = FileNameTextBlock?.Text;

            if (string.IsNullOrWhiteSpace(baseName))
                return "ConvertedCode.cs";

            try
            {
                var nameOnly = Path.GetFileNameWithoutExtension(baseName);
                if (string.IsNullOrWhiteSpace(nameOnly))
                    return "ConvertedCode.cs";

                return $"{SanitizeFileName(nameOnly)}.cs";
            }
            catch
            {
                return "ConvertedCode.cs";
            }
        }

        private static string SanitizeFileName(string name)
        {
            foreach (var c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');

            return string.IsNullOrWhiteSpace(name)
                ? "ConvertedCode"
                : name;
        }
    }
}
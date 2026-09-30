using System.Diagnostics;
using System.Drawing;
using System.Text;
using System.Text.RegularExpressions;

namespace NotepadPLUS;

public partial class Form1 : Form
{
    private readonly TabControl _tabs = new();
    private readonly ToolStripStatusLabel _cursorStatus = new()
    {
        Spring = true,
        TextAlign = ContentAlignment.MiddleLeft
    };
    private readonly ToolStripStatusLabel _hintsStatus = new();
    private readonly ToolStripMenuItem _previewMenuItem = new("&Markdown Preview")
    {
        CheckOnClick = true,
        ShortcutKeys = Keys.Control | Keys.Shift | Keys.M
    };
    private readonly System.Windows.Forms.Timer _highlightTimer = new() { Interval = 180 };
    private readonly Dictionary<RichTextBox, DocumentState> _documents = new();
    private readonly HashSet<RichTextBox> _pendingHighlightEditors = new();
    private readonly HashSet<RichTextBox> _pendingPreviewEditors = new();
    private int _untitledCounter;
    private bool _formatting;
    private bool _updatingPreviewMenu;

    public Form1()
    {
        InitializeComponent();
        Text = "NotepadPLUS";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(500, 350);

        var iconPath = Path.Combine(AppContext.BaseDirectory, "icon.ico");
        if (File.Exists(iconPath))
        {
            using var appIcon = new Icon(iconPath);
            Icon = (Icon)appIcon.Clone();
        }

        BuildInterface();
        _highlightTimer.Tick += (_, _) =>
        {
            _highlightTimer.Stop();

            var pendingEditors = _pendingHighlightEditors.ToArray();
            _pendingHighlightEditors.Clear();
            foreach (var editor in pendingEditors)
            {
                if (_documents.TryGetValue(editor, out var document) && document.IsMarkdown)
                {
                    ApplyMarkdownHints(editor);
                }
            }

            var pendingPreviews = _pendingPreviewEditors.ToArray();
            _pendingPreviewEditors.Clear();
            foreach (var editor in pendingPreviews)
            {
                if (_documents.TryGetValue(editor, out var document) && document.PreviewVisible)
                {
                    RenderMarkdownPreview(editor);
                }
            }
        };
        FormClosing += HandleFormClosing;
        CreateDocument();
    }

    private void BuildInterface()
    {
        var menuStrip = new MenuStrip();
        var fileMenu = new ToolStripMenuItem("&File");
        fileMenu.DropDownItems.Add(CreateMenuItem("&New", Keys.Control | Keys.N, (_, _) => CreateDocument()));
        fileMenu.DropDownItems.Add(CreateMenuItem("&Open...", Keys.Control | Keys.O, OpenFiles));
        fileMenu.DropDownItems.Add(new ToolStripSeparator());
        fileMenu.DropDownItems.Add(CreateMenuItem("&Save", Keys.Control | Keys.S, (_, _) => SaveDocument(GetActiveEditor())));
        fileMenu.DropDownItems.Add(CreateMenuItem("Save &As...", Keys.Control | Keys.Shift | Keys.S, (_, _) => SaveDocumentAs(GetActiveEditor())));
        fileMenu.DropDownItems.Add(new ToolStripSeparator());
        fileMenu.DropDownItems.Add(CreateMenuItem("&Close Tab", Keys.Control | Keys.W, (_, _) => CloseCurrentTab()));
        fileMenu.DropDownItems.Add(new ToolStripSeparator());
        fileMenu.DropDownItems.Add(CreateMenuItem("E&xit", Keys.Alt | Keys.F4, (_, _) => Close()));
        menuStrip.Items.Add(fileMenu);

        var viewMenu = new ToolStripMenuItem("&View");
        _previewMenuItem.CheckedChanged += HandlePreviewMenuCheckedChanged;
        viewMenu.DropDownItems.Add(_previewMenuItem);
        menuStrip.Items.Add(viewMenu);

        _tabs.Dock = DockStyle.Fill;
        _tabs.Multiline = true;
        _tabs.SelectedIndexChanged += (_, _) =>
        {
            UpdateStatusBar();
            UpdateWindowTitle();
            UpdatePreviewMenu();
        };

        var statusBar = new StatusStrip();
        statusBar.Items.Add(_cursorStatus);
        statusBar.Items.Add(_hintsStatus);

        Controls.Add(_tabs);
        Controls.Add(statusBar);
        Controls.Add(menuStrip);
        MainMenuStrip = menuStrip;
    }

    private static ToolStripMenuItem CreateMenuItem(string text, Keys shortcut, EventHandler action)
    {
        var item = new ToolStripMenuItem(text)
        {
            ShortcutKeys = shortcut,
            ShowShortcutKeys = true
        };
        item.Click += action;
        return item;
    }

    private void CreateDocument(string? filePath = null, string? contents = null)
    {
        var editor = new RichTextBox
        {
            AcceptsTab = true,
            BorderStyle = BorderStyle.None,
            DetectUrls = false,
            Dock = DockStyle.Fill,
            Font = new Font("Consolas", 11F),
            HideSelection = false,
            Multiline = true,
            ScrollBars = RichTextBoxScrollBars.Both,
            WordWrap = false
        };

        var preview = new WebBrowser
        {
            AllowWebBrowserDrop = false,
            Dock = DockStyle.Fill,
            IsWebBrowserContextMenuEnabled = false,
            ScriptErrorsSuppressed = true,
            WebBrowserShortcutsEnabled = false
        };
        preview.Navigating += HandlePreviewNavigating;

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            FixedPanel = FixedPanel.None,
            Orientation = Orientation.Vertical,
            Size = new Size(900, 540),
            SplitterDistance = 450,
            Panel1MinSize = 180,
            Panel2MinSize = 180
        };

        split.Panel1.Controls.Add(editor);
        split.Panel2.Controls.Add(preview);

        var displayName = filePath is null ? $"Untitled {++_untitledCounter}" : Path.GetFileName(filePath);
        var isMarkdown = filePath is null || IsMarkdownFile(filePath);
        var document = new DocumentState(filePath, displayName, isMarkdown, split, preview)
        {
            PreviewVisible = isMarkdown
        };
        _documents.Add(editor, document);

        var page = new TabPage(displayName);
        page.Controls.Add(split);
        _tabs.TabPages.Add(page);
        _tabs.SelectedTab = page;
        split.Panel2Collapsed = !document.PreviewVisible;

        editor.Text = contents ?? string.Empty;
        editor.TextChanged += HandleEditorTextChanged;
        editor.SelectionChanged += HandleEditorSelectionChanged;

        ApplyMarkdownHints(editor);
        if (document.PreviewVisible)
        {
            RenderMarkdownPreview(editor);
        }

        UpdateTabCaption(editor);
        UpdateWindowTitle();
        UpdatePreviewMenu();
    }

    private RichTextBox? GetActiveEditor()
    {
        return _tabs.SelectedTab?.Controls.OfType<SplitContainer>().FirstOrDefault()?.Panel1.Controls
            .OfType<RichTextBox>().FirstOrDefault();
    }

    private void HandleEditorTextChanged(object? sender, EventArgs e)
    {
        if (_formatting || sender is not RichTextBox editor || !_documents.TryGetValue(editor, out var document))
        {
            return;
        }

        document.IsDirty = true;
        UpdateTabCaption(editor);
        UpdateWindowTitle();
        UpdateStatusBar();

        if (document.IsMarkdown)
        {
            _pendingHighlightEditors.Add(editor);
        }

        if (document.PreviewVisible)
        {
            _pendingPreviewEditors.Add(editor);
        }

        if (_pendingHighlightEditors.Count > 0 || _pendingPreviewEditors.Count > 0)
        {
            _highlightTimer.Stop();
            _highlightTimer.Start();
        }
    }

    private void HandleEditorSelectionChanged(object? sender, EventArgs e)
    {
        if (!_formatting)
        {
            UpdateStatusBar();
        }
    }

    private void ApplyMarkdownHints(RichTextBox editor)
    {
        if (!_documents.TryGetValue(editor, out var document) || editor.IsDisposed)
        {
            return;
        }

        var selectionStart = editor.SelectionStart;
        var selectionLength = editor.SelectionLength;
        var text = editor.Text;
        _formatting = true;

        try
        {
            editor.SelectAll();
            editor.SelectionFont = editor.Font;
            editor.SelectionColor = Color.FromArgb(38, 42, 48);
            editor.SelectionBackColor = editor.BackColor;

            if (document.IsMarkdown)
            {
                ApplyPattern(editor, text, @"(?m)^#{1,6}\s+.*$", Color.FromArgb(38, 93, 160), FontStyle.Bold);
                ApplyPattern(editor, text, @"(?m)^>.*$", Color.FromArgb(105, 111, 120), FontStyle.Italic);
                ApplyPattern(editor, text, @"(?m)^\s*(?:[-*+]|\d+\.)", Color.FromArgb(40, 125, 90), FontStyle.Bold);
                ApplyPattern(editor, text, @"\*\*[^*\r\n]+\*\*|__[^_\r\n]+__", Color.FromArgb(38, 42, 48), FontStyle.Bold);
                ApplyPattern(editor, text, @"(?<!\*)\*(?!\*)[^*\r\n]+\*(?!\*)|(?<!_)_(?!_)[^_\r\n]+_(?!_)", Color.FromArgb(38, 42, 48), FontStyle.Italic);
                ApplyPattern(editor, text, @"\x60[^\r\n\x60]+\x60", Color.FromArgb(125, 65, 145), FontStyle.Regular, Color.FromArgb(246, 240, 249));
                ApplyPattern(editor, text, @"\[[^\]\r\n]+\]\([^) \r\n]+\)", Color.FromArgb(35, 105, 175), FontStyle.Underline);
            }
        }
        finally
        {
            var safeStart = Math.Min(selectionStart, editor.TextLength);
            var safeLength = Math.Min(selectionLength, editor.TextLength - safeStart);
            editor.Select(safeStart, safeLength);
            _formatting = false;
            UpdateStatusBar();
        }
    }

    private static void ApplyPattern(
        RichTextBox editor,
        string text,
        string pattern,
        Color color,
        FontStyle? fontStyle = null,
        Color? background = null)
    {
        foreach (Match match in Regex.Matches(text, pattern, RegexOptions.CultureInvariant))
        {
            editor.Select(match.Index, match.Length);
            editor.SelectionColor = color;

            if (fontStyle.HasValue)
            {
                using var font = new Font(editor.Font, fontStyle.Value);
                editor.SelectionFont = font;
            }

            if (background.HasValue)
            {
                editor.SelectionBackColor = background.Value;
            }
        }
    }

    private void RenderMarkdownPreview(RichTextBox editor)
    {
        if (_documents.TryGetValue(editor, out var document)
            && document.PreviewVisible
            && !document.Preview.IsDisposed)
        {
            document.Preview.DocumentText = MarkdownRenderer.ToHtmlDocument(editor.Text);
        }
    }

    private void HandlePreviewNavigating(object? sender, WebBrowserNavigatingEventArgs e)
    {
        if (e.Url is null || e.Url.Scheme.Equals("about", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        e.Cancel = true;
        if (e.Url.Scheme is not ("http" or "https" or "mailto"))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(e.Url.AbsoluteUri) { UseShellExecute = true });
        }
        catch
        {
            // Keep link launch errors from taking down the editor.
        }
    }

    private void HandlePreviewMenuCheckedChanged(object? sender, EventArgs e)
    {
        if (!_updatingPreviewMenu && GetActiveEditor() is { } editor)
        {
            SetPreviewVisible(editor, _previewMenuItem.Checked);
        }
    }

    private void SetPreviewVisible(RichTextBox editor, bool visible)
    {
        if (!_documents.TryGetValue(editor, out var document))
        {
            return;
        }

        document.PreviewVisible = visible;
        document.Split.Panel2Collapsed = !visible;
        if (visible)
        {
            RenderMarkdownPreview(editor);
        }

        UpdatePreviewMenu();
    }

    private void UpdatePreviewMenu()
    {
        var editor = GetActiveEditor();
        _updatingPreviewMenu = true;
        try
        {
            _previewMenuItem.Enabled = editor is not null;
            _previewMenuItem.Checked = editor is not null
                && _documents.TryGetValue(editor, out var document)
                && document.PreviewVisible;
        }
        finally
        {
            _updatingPreviewMenu = false;
        }
    }

    private void OpenFiles(object? sender, EventArgs e)
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "Markdown and text files (*.md;*.markdown;*.txt)|*.md;*.markdown;*.txt|All files (*.*)|*.*",
            Multiselect = true,
            Title = "Open files"
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        foreach (var filePath in dialog.FileNames)
        {
            var existingEditor = _documents.Keys.FirstOrDefault(editor =>
                _documents[editor].FilePath is { } openPath && PathsEqual(openPath, filePath));

            if (existingEditor is not null)
            {
                _tabs.SelectedTab = existingEditor.Parent?.Parent?.Parent as TabPage;
                continue;
            }

            try
            {
                CreateDocument(filePath, File.ReadAllText(filePath));
            }
            catch (Exception exception)
            {
                MessageBox.Show(this, $"Couldn't open the file.\n\n{exception.Message}", "Open file",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }

    private void SaveDocument(RichTextBox? editor)
    {
        if (editor is null || !_documents.TryGetValue(editor, out var document))
        {
            return;
        }

        if (document.FilePath is null)
        {
            SaveDocumentAs(editor);
            return;
        }

        SaveToPath(editor, document, document.FilePath);
    }

    private void SaveDocumentAs(RichTextBox? editor)
    {
        if (editor is null || !_documents.TryGetValue(editor, out var document))
        {
            return;
        }

        using var dialog = new SaveFileDialog
        {
            AddExtension = true,
            DefaultExt = "md",
            FileName = document.FilePath is null ? $"{document.DisplayName}.md" : Path.GetFileName(document.FilePath),
            Filter = "Markdown files (*.md)|*.md|Markdown files (*.markdown)|*.markdown|Text files (*.txt)|*.txt|All files (*.*)|*.*",
            OverwritePrompt = true,
            Title = "Save file"
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            SaveToPath(editor, document, dialog.FileName);
        }
    }

    private void SaveToPath(RichTextBox editor, DocumentState document, string filePath)
    {
        try
        {
            File.WriteAllText(filePath, editor.Text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            document.FilePath = filePath;
            document.DisplayName = Path.GetFileName(filePath);
            document.IsDirty = false;
            var wasMarkdown = document.IsMarkdown;
            document.IsMarkdown = IsMarkdownFile(filePath);
            if (!wasMarkdown && document.IsMarkdown)
            {
                document.PreviewVisible = true;
                document.Split.Panel2Collapsed = false;
            }

            ApplyMarkdownHints(editor);
            RenderMarkdownPreview(editor);
            UpdateTabCaption(editor);
            UpdateWindowTitle();
            UpdateStatusBar();
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, $"Couldn't save the file.\n\n{exception.Message}", "Save file",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void CloseCurrentTab()
    {
        if (_tabs.SelectedTab is not { } page || GetActiveEditor() is not { } editor)
        {
            return;
        }

        if (!ConfirmSaveIfNeeded(editor))
        {
            return;
        }

        _pendingHighlightEditors.Remove(editor);
        _pendingPreviewEditors.Remove(editor);
        _documents.Remove(editor);
        if (_pendingHighlightEditors.Count == 0 && _pendingPreviewEditors.Count == 0)
        {
            _highlightTimer.Stop();
        }
        _tabs.TabPages.Remove(page);
        page.Dispose();

        if (_tabs.TabCount == 0)
        {
            CreateDocument();
        }
        else
        {
            UpdateStatusBar();
            UpdateWindowTitle();
            UpdatePreviewMenu();
        }
    }

    private bool ConfirmSaveIfNeeded(RichTextBox editor)
    {
        var document = _documents[editor];
        if (!document.IsDirty)
        {
            return true;
        }

        var result = MessageBox.Show(this, $"Save changes to {document.DisplayName}?", "NotepadPLUS",
            MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);

        return result switch
        {
            DialogResult.Yes => SaveDocumentAndReport(editor),
            DialogResult.No => true,
            _ => false
        };
    }

    private bool SaveDocumentAndReport(RichTextBox editor)
    {
        var wasDirty = _documents[editor].IsDirty;
        SaveDocument(editor);
        return wasDirty && !_documents[editor].IsDirty;
    }

    private void HandleFormClosing(object? sender, FormClosingEventArgs e)
    {
        foreach (var editor in _documents.Keys.ToArray())
        {
            if (!ConfirmSaveIfNeeded(editor))
            {
                e.Cancel = true;
                return;
            }
        }
    }

    private void UpdateTabCaption(RichTextBox editor)
    {
        if (!_documents.TryGetValue(editor, out var document) || editor.Parent?.Parent?.Parent is not TabPage page)
        {
            return;
        }

        page.Text = $"{(document.IsDirty ? "*" : string.Empty)}{document.DisplayName}";
    }

    private void UpdateWindowTitle()
    {
        if (GetActiveEditor() is { } editor && _documents.TryGetValue(editor, out var document))
        {
            Text = $"NotepadPLUS - {(document.IsDirty ? "*" : string.Empty)}{document.DisplayName}";
        }
        else
        {
            Text = "NotepadPLUS";
        }
    }

    private void UpdateStatusBar()
    {
        if (GetActiveEditor() is not { } editor || !_documents.TryGetValue(editor, out var document))
        {
            _cursorStatus.Text = string.Empty;
            _hintsStatus.Text = string.Empty;
            return;
        }

        var line = editor.GetLineFromCharIndex(editor.SelectionStart);
        var firstCharacterOnLine = editor.GetFirstCharIndexFromLine(line);
        var column = firstCharacterOnLine < 0 ? editor.SelectionStart : editor.SelectionStart - firstCharacterOnLine;
        _cursorStatus.Text = $"Line {line + 1}, Col {column + 1}";
        _hintsStatus.Text = document.IsMarkdown ? "Markdown hints on" : "Markdown hints off";
    }

    private static bool IsMarkdownFile(string filePath)
    {
        var extension = Path.GetExtension(filePath);
        return extension.Equals(".md", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".markdown", StringComparison.OrdinalIgnoreCase);
    }

    private static bool PathsEqual(string left, string right)
    {
        return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _highlightTimer.Dispose();
        }

        base.Dispose(disposing);
    }

    private sealed class DocumentState(
        string? filePath,
        string displayName,
        bool isMarkdown,
        SplitContainer split,
        WebBrowser preview)
    {
        public string? FilePath { get; set; } = filePath;
        public string DisplayName { get; set; } = displayName;
        public bool IsMarkdown { get; set; } = isMarkdown;
        public bool IsDirty { get; set; }
        public bool PreviewVisible { get; set; }
        public SplitContainer Split { get; } = split;
        public WebBrowser Preview { get; } = preview;
    }
}

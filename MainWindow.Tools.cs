using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using PdfSharpCore.Pdf.IO;
using Scalpel.Services;

namespace Scalpel
{
    /// <summary>
    /// "Tools" features: page/Bates numbering, password protection, metadata sanitize,
    /// compression, redaction, and OCR. Kept in a partial file so the monolith stays untouched.
    /// All operations are 100% local (no online services beyond optional OCR-data download).
    /// </summary>
    public partial class MainWindow
    {
        // ---- shared document lifecycle helpers --------------------------------------------------

        /// <summary>Burns pending annotations/form values into a temp file and returns its path,
        /// leaving the live <c>_doc</c> intact and editable (mirrors the SaveFlattened pattern).</summary>
        private string BuildWorkingSourceFile()
        {
            CommitActiveTextBox();
            WriteFormValuesToDocument();
            StripLinkAnnotationBorders(_doc!);

            bool hasAnnotations = _annotations.Values.Any(list => list.Count > 0);
            if (hasAnnotations)
            {
                var tempClean = App.MakeTempFile("clean");
                var tempBurned = App.MakeTempFile("burned");
                _doc!.Save(tempClean);
                DrawAnnotationsOnDocument();
                _doc.Save(tempBurned);
                _doc.Close();
                _doc = PdfReader.Open(tempClean, PdfDocumentOpenMode.Modify);
                _currentFile = tempClean;
                return tempBurned;
            }

            var temp = App.MakeTempFile("src");
            _doc!.Save(temp);
            return temp;
        }

        /// <summary>Swaps in a transformed file as the new working document and refreshes the view.</summary>
        private void AdoptTransformedFile(string transformedPath, string statusMsg)
        {
            string display = _originalFile ?? transformedPath;
            if (_doc is not null) { _doc.Close(); _doc = null; }
            _doc = PdfReader.Open(transformedPath, PdfDocumentOpenMode.Modify);
            FinishOpenFile(display, transformedPath);
            MarkDirty(true); // working copy lives in temp — user must Save As
            SetStatus(statusMsg);
        }

        private bool RequireOpenDoc()
        {
            if (_doc is null || _currentFile is null)
            {
                ScalpelDialog.Show(this, "Open a PDF first.");
                return false;
            }
            return true;
        }

        /// <summary>Builds a small themed modal form from <paramref name="fields"/>; on OK, writes the
        /// user's input back into each field and returns true. An optional <paramref name="note"/>
        /// renders as a wrapped hint above the fields.</summary>
        private bool ShowToolForm(string title, IReadOnlyList<ToolField> fields, string okText, string? note = null)
        {
            var fontUI = (FontFamily)Application.Current.FindResource("FontUI");
            var bgModal = (Brush)Application.Current.FindResource("BgModal");
            var bgCtrl = (Brush)Application.Current.FindResource("BgControl");
            var fgPri = (Brush)Application.Current.FindResource("TextPrimary");
            var fgSec = (Brush)Application.Current.FindResource("TextSecondary");
            var bdrDim = (Brush)Application.Current.FindResource("BorderDim");
            var accent = (Brush)Application.Current.FindResource("Accent");
            double fsBody = (double)Application.Current.FindResource("FsBody");

            var win = new Window
            {
                Title = title, Width = 380, SizeToContent = SizeToContent.Height,
                WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent,
                WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = this,
                ResizeMode = ResizeMode.NoResize, FontFamily = fontUI,
            };

            var outerBorder = new Border
            {
                Background = bgModal, BorderBrush = bdrDim, BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                { Color = Colors.Black, BlurRadius = 24, ShadowDepth = 4, Opacity = 0.4, Direction = 270 },
            };

            var titleBar = new Border
            {
                Background = (Brush)Application.Current.FindResource("BgPanel"),
                Padding = new Thickness(16, 10, 8, 10), CornerRadius = new CornerRadius(11, 11, 0, 0),
            };
            titleBar.MouseLeftButtonDown += (_, ev) => { if (ev.ButtonState == MouseButtonState.Pressed) win.DragMove(); };
            var titleGrid = new Grid();
            titleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            titleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            titleGrid.Children.Add(new TextBlock
            {
                Text = title, Foreground = fgPri, FontWeight = FontWeights.SemiBold,
                FontSize = (double)Application.Current.FindResource("FsDialogTitle"),
                FontFamily = fontUI, VerticalAlignment = VerticalAlignment.Center,
            });
            var closeBtn = new Button
            {
                Style = (Style)Application.Current.FindResource("StudioIconButton"),
                Content = Application.Current.FindResource("Ico_WinClose"),
            };
            closeBtn.Click += (_, __) => win.DialogResult = false;
            Grid.SetColumn(closeBtn, 1);
            titleGrid.Children.Add(closeBtn);
            titleBar.Child = titleGrid;

            var sp = new StackPanel { Margin = new Thickness(20, 16, 20, 16) };
            if (!string.IsNullOrEmpty(note))
            {
                sp.Children.Add(new TextBlock
                {
                    Text = note, Foreground = fgSec, FontFamily = fontUI, FontSize = fsBody,
                    TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 14),
                });
            }
            var controls = new List<(ToolField field, FrameworkElement ctrl)>();
            foreach (var f in fields)
            {
                if (f.Kind == ToolFieldKind.Check)
                {
                    var cb = new CheckBox
                    {
                        Content = f.Label, IsChecked = f.Checked, Foreground = fgPri,
                        FontFamily = fontUI, FontSize = fsBody, Margin = new Thickness(0, 2, 0, 10),
                    };
                    sp.Children.Add(cb);
                    controls.Add((f, cb));
                    continue;
                }

                sp.Children.Add(new TextBlock
                {
                    Text = f.Label, Foreground = fgSec, FontFamily = fontUI,
                    FontSize = fsBody, Margin = new Thickness(0, 0, 0, 3),
                });

                FrameworkElement ctrl;
                if (f.Kind == ToolFieldKind.Combo)
                {
                    var combo = new ComboBox { Margin = new Thickness(0, 0, 0, 12), FontFamily = fontUI };
                    var opts = f.Options ?? Array.Empty<string>();
                    foreach (var o in opts) combo.Items.Add(o);
                    int idx = Array.IndexOf(opts, f.Value);
                    combo.SelectedIndex = idx >= 0 ? idx : 0;
                    ctrl = combo;
                }
                else if (f.Kind == ToolFieldKind.Password)
                {
                    ctrl = new PasswordBox
                    {
                        Margin = new Thickness(0, 0, 0, 12), Background = bgCtrl, Foreground = fgPri,
                        BorderBrush = bdrDim, CaretBrush = accent, Padding = new Thickness(8, 6, 8, 6),
                    };
                }
                else
                {
                    ctrl = new TextBox
                    {
                        Text = f.Value, Margin = new Thickness(0, 0, 0, 12), Background = bgCtrl,
                        Foreground = fgPri, BorderBrush = bdrDim, CaretBrush = accent,
                        Padding = new Thickness(8, 6, 8, 6), FontFamily = fontUI,
                    };
                }
                sp.Children.Add(ctrl);
                controls.Add((f, ctrl));
            }

            var btnRow = new StackPanel
            {
                Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 4, 0, 0),
            };
            var cancelBtn = new Button
            {
                Content = "Cancel", Style = (Style)Application.Current.FindResource("StudioToolButton"),
                Width = 90, Margin = new Thickness(0, 0, 8, 0),
            };
            var okBtn = new Button
            {
                Content = okText, Style = (Style)Application.Current.FindResource("StudioPrimaryButton"), Width = 120,
            };
            cancelBtn.Click += (_, __) => win.DialogResult = false;
            okBtn.Click += (_, __) => win.DialogResult = true;
            btnRow.Children.Add(cancelBtn);
            btnRow.Children.Add(okBtn);
            sp.Children.Add(btnRow);

            var root = new StackPanel();
            root.Children.Add(titleBar);
            root.Children.Add(sp);
            outerBorder.Child = root;
            win.Content = outerBorder;

            if (win.ShowDialog() != true) return false;

            foreach (var (field, ctrl) in controls)
            {
                switch (ctrl)
                {
                    case CheckBox cb: field.Checked = cb.IsChecked == true; break;
                    case ComboBox combo: field.Value = combo.SelectedItem?.ToString() ?? ""; break;
                    case PasswordBox pb: field.Value = pb.Password; break;
                    case TextBox tb: field.Value = tb.Text; break;
                }
            }
            return true;
        }

        // ---- Tools menu handlers ----------------------------------------------------------------


        private static string TryInfo(Func<string?> get)
        {
            try { return get() ?? ""; } catch { return ""; }
        }

        private void ShowDocumentInfo()
        {
            if (!RequireOpenDoc()) return;

            string title    = TryInfo(() => _doc!.Info.Title);
            string author   = TryInfo(() => _doc!.Info.Author);
            string subject  = TryInfo(() => _doc!.Info.Subject);
            string keywords = TryInfo(() => _doc!.Info.Keywords);
            string creator  = TryInfo(() => _doc!.Info.Creator);

            // read-only summary
            var parts = new List<string>();
            string producer = TryInfo(() => _doc!.Info.Producer);
            if (producer.Length > 0) parts.Add($"{Loc("Str_DocInfo_Producer")}: {producer}");
            try { parts.Add($"{_doc!.PageCount} {Loc("Str_DocInfo_Pages")}  ·  PDF {_doc.Version / 10}.{_doc.Version % 10}"); } catch { }
            try { var d = _doc!.Info.CreationDate; if (d != default) parts.Add($"{Loc("Str_DocInfo_Created")} {d:yyyy-MM-dd HH:mm}"); } catch { }
            try { var p = _originalFile ?? _currentFile; if (!string.IsNullOrEmpty(p) && File.Exists(p)) parts.Add($"{new FileInfo(p).Length / 1024.0:N0} KB"); } catch { }
            string note = string.Join("\n", parts);

            var fTitle    = new ToolField(Loc("Str_DocInfo_FldTitle"),    ToolFieldKind.Text, value: title);
            var fAuthor   = new ToolField(Loc("Str_DocInfo_FldAuthor"),   ToolFieldKind.Text, value: author);
            var fSubject  = new ToolField(Loc("Str_DocInfo_FldSubject"),  ToolFieldKind.Text, value: subject);
            var fKeywords = new ToolField(Loc("Str_DocInfo_FldKeywords"), ToolFieldKind.Text, value: keywords);
            var fCreator  = new ToolField(Loc("Str_DocInfo_FldCreator"),  ToolFieldKind.Text, value: creator);

            if (!ShowToolForm(Loc("Str_Tool_DocInfo"), new[] { fTitle, fAuthor, fSubject, fKeywords, fCreator },
                              Loc("Str_DocInfo_Save"), string.IsNullOrEmpty(note) ? null : note))
                return;

            _doc!.Info.Title    = fTitle.Value;
            _doc.Info.Author    = fAuthor.Value;
            _doc.Info.Subject   = fSubject.Value;
            _doc.Info.Keywords  = fKeywords.Value;
            _doc.Info.Creator   = fCreator.Value;
            MarkDirty(true);
            SetStatus(Loc("Str_DocInfo_Updated"));
        }

        private void ToolsDocumentInfo_Click(object sender, RoutedEventArgs e) => ShowDocumentInfo();

        private void ToolsNumbering_Click(object sender, RoutedEventArgs e)
        {
            if (!RequireOpenDoc()) return;

            var mode = new ToolField("Mode", ToolFieldKind.Combo,
                options: new[] { "Page X of N", "Bates number", "Custom header/footer" });
            var text = new ToolField("Text / prefix", ToolFieldKind.Text, value: "");
            var pos = new ToolField("Position", ToolFieldKind.Combo,
                options: Enum.GetNames(typeof(StampPosition)), value: nameof(StampPosition.BottomRight));
            var start = new ToolField("Start number", ToolFieldKind.Int, value: "1");
            var digits = new ToolField("Digits (0 = none)", ToolFieldKind.Int, value: "6");

            if (!ShowToolForm("Add Numbering / Bates / Header", new[] { mode, text, pos, start, digits }, "Apply"))
                return;

            string template = mode.Value switch
            {
                "Bates number" => (text.Value ?? "") + "{n}",
                "Custom header/footer" => text.Value ?? "",
                _ => "Page {page} of {total}",
            };
            var opts = new StampOptions
            {
                Template = template,
                Position = (StampPosition)Enum.Parse(typeof(StampPosition), pos.Value),
                StartNumber = ParseInt(start.Value, 1),
                DigitCount = Math.Max(0, ParseInt(digits.Value, 0)),
            };

            RunFileTransform("Applying numbering…", src =>
            {
                var outPath = App.MakeTempFile("numbered");
                BatesNumberingService.StampFile(src, outPath, opts);
                return outPath;
            }, "Numbering applied.");
        }

        private void ToolsWatermark_Click(object sender, RoutedEventArgs e)
        {
            if (!RequireOpenDoc()) return;

            var text = new ToolField("Watermark text", ToolFieldKind.Text, value: "CONFIDENTIAL");
            var pos = new ToolField("Position", ToolFieldKind.Combo,
                options: Enum.GetNames(typeof(WatermarkPosition)), value: nameof(WatermarkPosition.Tiled));
            var opacity = new ToolField("Opacity (%)", ToolFieldKind.Int, value: "30");
            var fontSize = new ToolField("Font size", ToolFieldKind.Int, value: "48");
            var rotation = new ToolField("Rotation (°)", ToolFieldKind.Int, value: "45");
            var addImage = new ToolField("Also stamp an image / logo…", ToolFieldKind.Check, isChecked: false);

            if (!ShowToolForm("Watermark / Stamp", new[] { text, pos, opacity, fontSize, rotation, addImage }, "Apply",
                    note: "Stamps a semi-transparent text watermark across the pages. Optionally add an image " +
                          "or logo stamp too. Applies to the working copy — use Save As to write it out."))
                return;

            string? imagePath = null;
            if (addImage.Checked)
            {
                var ofd = new OpenFileDialog
                {
                    Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif|All files|*.*",
                    Title = "Choose an image to stamp",
                };
                if (ofd.ShowDialog(this) == true) imagePath = ofd.FileName;
            }

            if (string.IsNullOrWhiteSpace(text.Value) && string.IsNullOrEmpty(imagePath))
            {
                ScalpelDialog.Show(this, "Enter watermark text or choose an image to stamp.");
                return;
            }

            var opts = new WatermarkOptions
            {
                Text = string.IsNullOrWhiteSpace(text.Value) ? null : text.Value,
                Position = (WatermarkPosition)Enum.Parse(typeof(WatermarkPosition), pos.Value),
                Opacity = Math.Max(0, Math.Min(100, ParseInt(opacity.Value, 30))) / 100.0,
                FontSize = Math.Max(1, ParseInt(fontSize.Value, 48)),
                RotationDegrees = ParseInt(rotation.Value, 45),
                ImagePath = imagePath,
            };

            RunFileTransform("Applying watermark…", src =>
            {
                var outPath = App.MakeTempFile("watermarked");
                WatermarkService.ApplyFile(src, outPath, opts);
                return outPath;
            }, "Watermark applied.");
        }

        private void ToolsTransform_Click(object sender, RoutedEventArgs e)
        {
            if (!RequireOpenDoc()) return;

            var rotate = new ToolField(Loc("Str_Tf_Rotate"), ToolFieldKind.Combo,
                options: new[] { "0°", "90° CW", "180°", "270° CW" }, value: "0°");
            var fine = new ToolField(Loc("Str_Tf_FineAngle"), ToolFieldKind.Int, value: "0");
            var scale = new ToolField(Loc("Str_Tf_Scale"), ToolFieldKind.Int, value: "100");
            var flipH = new ToolField(Loc("Str_Tf_FlipH"), ToolFieldKind.Check, isChecked: false);
            var flipV = new ToolField(Loc("Str_Tf_FlipV"), ToolFieldKind.Check, isChecked: false);
            var resize = new ToolField(Loc("Str_Tf_ResizePage"), ToolFieldKind.Check, isChecked: true);
            var range = new ToolField(Loc("Str_Tf_Range"), ToolFieldKind.Text, value: "");

            if (!ShowToolForm(Loc("Str_Tool_Transform"),
                    new[] { rotate, fine, scale, flipH, flipV, resize, range }, Loc("Str_Tf_Apply"),
                    note: Loc("Str_Tf_Note")))
                return;

            int turns = rotate.Value switch
            {
                "90° CW" => 1,
                "180°" => 2,
                "270° CW" => 3,
                _ => 0,
            };
            double fineAngle = Math.Max(-45, Math.Min(45, ParseInt(fine.Value, 0)));
            double scaleFactor = Math.Max(10, Math.Min(400, ParseInt(scale.Value, 100))) / 100.0;
            var (fromPage, toPage) = ParsePageRange(range.Value);

            var opts = new TransformOptions
            {
                QuarterTurns = turns,
                FineAngleDegrees = fineAngle,
                Scale = scaleFactor,
                FlipHorizontal = flipH.Checked,
                FlipVertical = flipV.Checked,
                ResizePage = resize.Checked,
                FromPage = fromPage,
                ToPage = toPage,
            };

            if (turns == 0 && Math.Abs(fineAngle) < 0.001 && Math.Abs(scaleFactor - 1.0) < 0.001
                && !flipH.Checked && !flipV.Checked)
            {
                ScalpelDialog.Show(this, Loc("Str_Tf_NothingToDo"));
                return;
            }

            RunFileTransform(Loc("Str_Tf_Working"), src =>
            {
                var outPath = App.MakeTempFile("transformed");
                TransformService.ApplyFile(src, outPath, opts);
                return outPath;
            }, Loc("Str_Tf_Done"));
        }

        /// <summary>Parses a "from-to" / "from" page range string into 1-based inclusive bounds
        /// (null on either side = open-ended). Blank or unparseable = full document.</summary>
        private static (int? from, int? to) ParsePageRange(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return (null, null);
            var parts = s!.Split(new[] { '-', '–', ':' }, 2);
            int? from = int.TryParse(parts[0].Trim(), out int a) && a > 0 ? a : (int?)null;
            int? to = parts.Length > 1 && int.TryParse(parts[1].Trim(), out int b) && b > 0 ? b : (int?)null;
            return (from, to);
        }

        private void ToolsProtect_Click(object sender, RoutedEventArgs e)
        {
            if (!RequireOpenDoc()) return;

            var pw = new ToolField("Password", ToolFieldKind.Password);
            var confirm = new ToolField("Confirm password", ToolFieldKind.Password);
            var allowPrint = new ToolField("Allow printing", ToolFieldKind.Check, isChecked: true);
            var allowCopy = new ToolField("Allow copying text", ToolFieldKind.Check, isChecked: true);

            if (!ShowToolForm("Password Protect PDF", new[] { pw, confirm, allowPrint, allowCopy }, "Protect"))
                return;

            if (string.IsNullOrEmpty(pw.Value))
            {
                ScalpelDialog.Show(this, "Password cannot be empty.");
                return;
            }
            if (pw.Value != confirm.Value)
            {
                ScalpelDialog.Show(this, "Passwords do not match.");
                return;
            }

            var dlg = new SaveFileDialog { Filter = "PDF files|*.pdf", Title = "Save protected PDF as",
                                           CheckFileExists = false, CheckPathExists = true };
            if (!string.IsNullOrEmpty(_originalFile))
                dlg.FileName = System.IO.Path.GetFileNameWithoutExtension(_originalFile) + "-protected.pdf";
            if (dlg.ShowDialog(this) != true) return;

            try
            {
                string src = BuildWorkingSourceFile();
                PdfEncryptionService.Protect(src, dlg.FileName, new EncryptionOptions
                {
                    UserPassword = pw.Value,
                    AllowPrint = allowPrint.Checked,
                    AllowCopy = allowCopy.Checked,
                });
                SetStatus($"Saved password-protected copy to {System.IO.Path.GetFileName(dlg.FileName)}");
                ScalpelDialog.Show(this, "Saved a password-protected copy. Keep your password safe — it cannot be recovered.");
            }
            catch (Exception ex)
            {
                ScalpelDialog.Show(this, $"Could not protect the PDF:\n{ex.Message}", "Scalpel",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ToolsSign_Click(object sender, RoutedEventArgs e)
        {
            if (!RequireOpenDoc()) return;

            // 1) Choose the certificate source: a .pfx/.p12 file, or the Windows certificate store
            //    (the latter only offered when the store is available — i.e. on Windows).
            bool storeAvailable = Scalpel.Services.Signing.WindowsCertificateStore.IsAvailable;
            string fileOpt = Loc("Str_Sign_SourceFile");
            string storeOpt = Loc("Str_Sign_SourceStore");
            var sourceField = new ToolField(Loc("Str_Sign_Source"), ToolFieldKind.Combo,
                value: fileOpt,
                options: storeAvailable ? new[] { fileOpt, storeOpt } : new[] { fileOpt });
            if (!ShowToolForm(Loc("Str_Tool_Sign"), new[] { sourceField }, Loc("Str_Sign_Continue"),
                    note: Loc("Str_Sign_Note")))
                return;

            // 2) Acquire the signer — either a selected store certificate (+chain) or a .pfx path+password.
            System.Security.Cryptography.X509Certificates.X509Certificate2? storeSigner = null;
            System.Security.Cryptography.X509Certificates.X509Certificate2[]? storeChain = null;
            string? pfxPath = null, pfxPassword = null;

            if (storeAvailable && sourceField.Value == storeOpt)
            {
                var certs = Scalpel.Services.Signing.WindowsCertificateStore.ListSigningCertificates();
                if (certs.Count == 0) { ScalpelDialog.Show(this, Loc("Str_Sign_NoCerts")); return; }
                var labels = certs.Select(Scalpel.Services.Signing.WindowsCertificateStore.Describe).ToArray();
                var certField = new ToolField(Loc("Str_Sign_CertLabel"), ToolFieldKind.Combo,
                    value: labels[0], options: labels);
                if (!ShowToolForm(Loc("Str_Tool_Sign"), new[] { certField }, Loc("Str_Sign_Apply"))) return;
                int idx = Array.IndexOf(labels, certField.Value);
                if (idx < 0) idx = 0;
                storeSigner = certs[idx];
                storeChain = Scalpel.Services.Signing.WindowsCertificateStore.BuildChain(storeSigner);
            }
            else
            {
                var ofd = new OpenFileDialog
                {
                    Filter = "Certificate files|*.pfx;*.p12|All files|*.*",
                    Title = Loc("Str_Sign_PickCert"),
                };
                if (ofd.ShowDialog(this) != true) return;
                var pw = new ToolField(Loc("Str_Sign_Password"), ToolFieldKind.Password);
                if (!ShowToolForm(Loc("Str_Tool_Sign"), new[] { pw }, Loc("Str_Sign_Apply"))) return;
                pfxPath = ofd.FileName; pfxPassword = pw.Value;
            }

            // 3) Choose where to write the signed copy.
            var dlg = new SaveFileDialog
            {
                Filter = "PDF files|*.pdf", Title = Loc("Str_Sign_SaveAs"),
                CheckFileExists = false, CheckPathExists = true,
            };
            if (!string.IsNullOrEmpty(_originalFile))
                dlg.FileName = System.IO.Path.GetFileNameWithoutExtension(_originalFile) + "-signed.pdf";
            if (dlg.ShowDialog(this) != true) return;

            // Sign the already-saved bytes by appending an incremental update — the document is
            // NOT re-serialized, so the signed /ByteRange stays valid. Written straight to the
            // user's chosen path (never adopted as the working copy, which would re-save it).
            try
            {
                SetStatus(Loc("Str_Sign_Working"));
                string src = BuildWorkingSourceFile();
                if (storeSigner is not null)
                    PdfSigningService.SignFileWithCertificate(src, dlg.FileName, storeSigner, storeChain);
                else
                    PdfSigningService.SignFile(src, dlg.FileName, pfxPath!, pfxPassword);
                SetStatus(string.Format(Loc("Str_Sign_Done"), System.IO.Path.GetFileName(dlg.FileName)));
                ScalpelDialog.Show(this, Loc("Str_Sign_DoneMsg"));
            }
            catch (Exception ex)
            {
                Scalpel.Services.Logger.Error("Tools", "sign.fail", ex.Message, ex);
                SetStatus(Loc("Str_Sign_Failed"));
                ScalpelDialog.Show(this, $"{Loc("Str_Sign_Failed")}:\n{ex.Message}", "Scalpel",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ToolsSanitize_Click(object sender, RoutedEventArgs e)
        {
            if (!RequireOpenDoc()) return;
            if (ScalpelDialog.Show(this,
                    "Remove document metadata (author, title, subject, keywords, and hidden XMP data)?\n\nThis applies to the working copy; use Save As to write it out.",
                    "Remove Metadata", MessageBoxButton.OKCancel, MessageBoxImage.None) != MessageBoxResult.OK)
                return;

            RunFileTransform("Removing metadata…", src =>
            {
                var outPath = App.MakeTempFile("sanitized");
                MetadataSanitizer.SanitizeFile(src, outPath);
                return outPath;
            }, "Metadata removed.");
        }

        private async void ToolsCompress_Click(object sender, RoutedEventArgs e)
        {
            if (!RequireOpenDoc()) return;

            var quality = new ToolField("Strength", ToolFieldKind.Combo,
                options: new[] { "Low (best quality)", "Medium", "High (smallest file)" }, value: "Medium");
            if (!ShowToolForm("Compress PDF", new[] { quality }, "Compress",
                    note: "Best for scanned or photo-heavy PDFs. Each page becomes an image, so a mostly-text " +
                          "document may not shrink — and can even get larger."))
                return;

            CompressionOptions opts = quality.Value switch
            {
                "Low (best quality)" => CompressionOptions.Low,
                "High (smallest file)" => CompressionOptions.High,
                _ => CompressionOptions.Medium,
            };

            // Whole flow inside one try so any managed failure (building the working copy, the
            // native rasterization, or adopting the result) shows a dialog instead of crashing.
            string outPath = App.MakeTempFile("compressed");
            SetStatus("Compressing…");
            try
            {
                string src = BuildWorkingSourceFile();
                long before = SafeLen(src);
                await Task.Run(() =>
                {
                    using var rasterizer = new DocnetPageRasterizer(src, 2200);
                    PdfCompressionService.Compress(rasterizer, opts, outPath);
                });
                long after = SafeLen(outPath);
                AdoptTransformedFile(outPath,
                    $"Compressed {FmtSize(before)} → {FmtSize(after)} ({Pct(before, after)}). Text is now image-based.");
            }
            catch (Exception ex)
            {
                Scalpel.Services.Logger.Error("Tools", "compress.fail", ex.Message, ex);
                SetStatus("Compression failed");
                ScalpelDialog.Show(this, $"Compression failed:\n{ex.Message}", "Scalpel",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task<(string exe, string tessdata, string lang)?> EnsureOcrReady()
        {
            string lang = App.GetSetting("OcrLanguage") ?? "eng";
            bool best = App.GetSetting("OcrHighQuality") == "1";

            string? exe = OcrAssets.FindTesseractExe();
            if (exe is null)
            {
                ScalpelDialog.Show(this,
                    "OCR needs the free Tesseract engine, which isn't installed.\n\nInstall it from https://github.com/UB-Mannheim/tesseract (or place tesseract.exe in %LOCALAPPDATA%\\Scalpel\\ocr) and try again.",
                    "OCR Engine Needed", MessageBoxButton.OK, MessageBoxImage.None);
                return null;
            }
            if (!OcrAssets.HasLanguage(lang, best))
            {
                if (ScalpelDialog.Show(this,
                        $"Download the '{lang}' OCR language data{(best ? " (high quality)" : "")} once into %LOCALAPPDATA%\\Scalpel\\ocr?\n\nThis is a one-time local download; everything stays on your machine.",
                        "Download OCR Data", MessageBoxButton.OKCancel, MessageBoxImage.None) != MessageBoxResult.OK)
                    return null;
                SetStatus("Downloading OCR language data…");
                bool ok = await Task.Run(() => OcrAssets.DownloadLanguage(lang, best));
                if (!ok)
                {
                    ScalpelDialog.Show(this, "Could not download the OCR language data. Check your connection and try again.",
                        "Scalpel", MessageBoxButton.OK, MessageBoxImage.Error);
                    return null;
                }
            }
            return (exe, OcrAssets.ResolveTessdataDir(lang, best), lang);
        }

        // Cancels the in-progress OCR run; null when none is running.
        private System.Threading.CancellationTokenSource? _ocrCts;

        // Cancel button on the OCR progress overlay (also reachable via Esc — see OnPreviewKeyDown).
        private void OcrProgressCancel_Click(object sender, RoutedEventArgs e) => _ocrCts?.Cancel();

        private async void ToolsOcr_Click(object sender, RoutedEventArgs e)
        {
            if (!RequireOpenDoc()) return;
            var r = await EnsureOcrReady(); if (r is null) return;

            // Whole flow inside one try so any managed failure (building the working copy, the
            // native rasterization/OCR, or adopting the result) shows a dialog instead of crashing.
            string outPath = App.MakeTempFile("ocr");
            _ocrCts = new System.Threading.CancellationTokenSource();
            var token = _ocrCts.Token;
            OcrProgressText.Text = Loc("Str_Ocr_Progress_Starting");
            OcrProgressOverlay.Visibility = Visibility.Visible;
            SetStatus("Running OCR — making text searchable…");
            try
            {
                string src = BuildWorkingSourceFile();
                await Task.Run(() =>
                {
                    using var rasterizer = new DocnetPageRasterizer(src, 2000);
                    var engine = new TesseractCliOcrEngine(r.Value.exe, r.Value.tessdata, r.Value.lang);
                    OcrService.MakeSearchable(rasterizer, engine, outPath,
                        onProgress: (cur, total) => Dispatcher.BeginInvoke((Action)(() =>
                            OcrProgressText.Text = string.Format(Loc("Str_Ocr_Progress_Page"), cur, total))),
                        cancel: token);
                }, token);
                OcrProgressOverlay.Visibility = Visibility.Collapsed;
                AdoptTransformedFile(outPath, "OCR complete — the document text is now selectable and searchable.");
            }
            catch (OperationCanceledException)
            {
                OcrProgressOverlay.Visibility = Visibility.Collapsed;
                SetStatus("OCR cancelled");
                try { System.IO.File.Delete(outPath); } catch { }
            }
            catch (Exception ex)
            {
                OcrProgressOverlay.Visibility = Visibility.Collapsed;
                Scalpel.Services.Logger.Error("Tools", "ocr.fail", ex.Message, ex);
                SetStatus("OCR failed");
                ScalpelDialog.Show(this, $"OCR failed:\n{ex.Message}", "Scalpel",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _ocrCts?.Dispose();
                _ocrCts = null;
            }
        }

        private void ToolsOcrLanguage_Click(object sender, RoutedEventArgs e)
        {
            string curLang = App.GetSetting("OcrLanguage") ?? "eng";
            bool curBest = App.GetSetting("OcrHighQuality") == "1";
            string curName = OcrAssets.Languages.FirstOrDefault(l => l.Code == curLang).Name ?? "English";

            var lang = new ToolField(Loc("Str_Ocr_LangLabel"), ToolFieldKind.Combo,
                value: curName, options: OcrAssets.Languages.Select(l => l.Name).ToArray());
            string fast = Loc("Str_Ocr_QualityFast"), bestL = Loc("Str_Ocr_QualityBest");
            var qual = new ToolField(Loc("Str_Ocr_QualityLabel"), ToolFieldKind.Combo,
                value: curBest ? bestL : fast, options: new[] { fast, bestL });

            if (!ShowToolForm(Loc("Str_Tool_OcrLanguage"), new[] { lang, qual }, Loc("Str_DocInfo_Save"))) return;

            string code = OcrAssets.Languages.FirstOrDefault(l => l.Name == lang.Value).Code ?? "eng";
            App.SetSetting("OcrLanguage", code);
            App.SetSetting("OcrHighQuality", qual.Value == bestL ? "1" : "0");
            SetStatus(Loc("Str_Ocr_LangSaved"));
        }

        private async void ToolsOcrPageToClipboard_Click(object sender, RoutedEventArgs e)
        {
            if (!RequireOpenDoc()) return;
            var r = await EnsureOcrReady(); if (r is null) return;
            int page = System.Math.Max(0, PageList.SelectedIndex);
            SetStatus("Running OCR…");
            try
            {
                var src = App.MakeTempFile("ocrclip"); _doc!.Save(src);
                string text = await Task.Run(() =>
                {
                    using var rast = new DocnetPageRasterizer(src, 2000);
                    var raster = rast.RenderPage(page);
                    var (wPt, hPt) = rast.PageSizePt(page);
                    var ocr = new TesseractCliOcrEngine(r.Value.exe, r.Value.tessdata, r.Value.lang)
                        .Recognize(raster.ImageBytes, wPt, hPt);
                    return OcrTextJoiner.Join(ocr.Words);
                });
                if (string.IsNullOrWhiteSpace(text)) { SetStatus("No text recognized on this page"); return; }
                Clipboard.SetText(text);
                SetStatus(Loc("Str_Ocr_Copied"));
            }
            catch (Exception ex)
            {
                Scalpel.Services.Logger.Error("Tools", "ocr.clip.fail", ex.Message, ex);
                ScalpelDialog.Show(this, $"OCR failed:\n{ex.Message}", "Scalpel", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ── OCR region: drag a rectangle, OCR just that area to the clipboard ──────────────────
        private bool _ocrRegionArmed;
        private int _ocrRegionPage;

        private void ToolsOcrRegion_Click(object sender, RoutedEventArgs e)
        {
            if (!RequireOpenDoc()) return;
            _ocrRegionArmed = true;
            Mouse.OverrideCursor = Cursors.Cross;
            SetStatus(Loc("Str_Ocr_RegionArmed"));
        }

        /// <summary>
        /// Mouse-up commit of an armed OCR-region drag. <paramref name="canvasRect"/> is in the page's
        /// canvas space; it is mapped to native page fractions (rotation-aware via <c>CanvasToPdfRect</c>)
        /// and OCR'd off-thread, with the recognized text placed on the clipboard.
        /// </summary>
        private async void FinishOcrRegion(int pageIdx, Rect canvasRect, double dragW, double dragH)
        {
            _ocrRegionArmed = false;
            Mouse.OverrideCursor = null;
            if (dragW < 8 || dragH < 8) { SetStatus(Loc("Str_Ocr_RegionTooSmall")); return; }
            if (_doc is null || pageIdx < 0 || pageIdx >= _doc.PageCount) return;
            if (!_renderDims.TryGetValue(pageIdx, out var dims)) return;

            var r = await EnsureOcrReady(); if (r is null) return;

            _pageRotations.TryGetValue(pageIdx, out int rot);
            var page = _doc.Pages[pageIdx];
            double pdfW = page.Width.Point, pdfH = page.Height.Point;
            var (x1, y1, x2, y2) = CanvasToPdfRect(canvasRect, pdfW, pdfH, dims.w, dims.h, rot);
            // Native top-left-origin fractions (PDF y is bottom-up; the raster is top-down).
            double fracX = x1 / pdfW;
            double fracW = (x2 - x1) / pdfW;
            double fracY = (pdfH - y2) / pdfH;
            double fracH = (y2 - y1) / pdfH;

            SetStatus("Running OCR…");
            try
            {
                var src = App.MakeTempFile("ocrregion"); _doc!.Save(src);
                string text = await Task.Run(() =>
                {
                    using var rast = new DocnetPageRasterizer(src, 3000);
                    var engine = new TesseractCliOcrEngine(r.Value.exe, r.Value.tessdata, r.Value.lang);
                    return OcrService.RecognizeRegionText(rast, engine, pageIdx, fracX, fracY, fracW, fracH, rot);
                });
                if (string.IsNullOrWhiteSpace(text)) { SetStatus(Loc("Str_Ocr_RegionEmpty")); return; }
                Clipboard.SetText(text);
                SetStatus(Loc("Str_Ocr_Copied"));
            }
            catch (Exception ex)
            {
                Scalpel.Services.Logger.Error("Tools", "ocr.region.fail", ex.Message, ex);
                ScalpelDialog.Show(this, $"OCR failed:\n{ex.Message}", "Scalpel", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void ToolsOcrExtractText_Click(object sender, RoutedEventArgs e)
        {
            if (!RequireOpenDoc()) return;
            var r = await EnsureOcrReady(); if (r is null) return;
            var dlg = new SaveFileDialog { Filter = "Text|*.txt|Markdown|*.md", FileName = "extracted-text.txt" };
            if (dlg.ShowDialog() != true) return;
            string outPath = dlg.FileName;
            bool md = outPath.EndsWith(".md", StringComparison.OrdinalIgnoreCase);
            try
            {
                var src = App.MakeTempFile("ocrtxt"); _doc!.Save(src);
                await Task.Run(() =>
                {
                    using var rast = new DocnetPageRasterizer(src, 2000);
                    var engine = new TesseractCliOcrEngine(r.Value.exe, r.Value.tessdata, r.Value.lang);
                    var sb = new System.Text.StringBuilder();
                    for (int i = 0; i < rast.PageCount; i++)
                    {
                        Dispatcher.Invoke(() => SetStatus($"OCR page {i + 1} of {rast.PageCount}…"));
                        var raster = rast.RenderPage(i);
                        var (wPt, hPt) = rast.PageSizePt(i);
                        var text = OcrTextJoiner.Join(engine.Recognize(raster.ImageBytes, wPt, hPt).Words);
                        if (md) sb.Append("## Page ").Append(i + 1).Append("\n\n");
                        sb.Append(text).Append("\n\n");
                    }
                    File.WriteAllText(outPath, sb.ToString().TrimEnd() + "\n");
                });
                SetStatus(Loc("Str_Ocr_ExtractDone"));
            }
            catch (Exception ex)
            {
                Scalpel.Services.Logger.Error("Tools", "ocr.extract.fail", ex.Message, ex);
                ScalpelDialog.Show(this, $"OCR failed:\n{ex.Message}", "Scalpel", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void ToolsRedact_Click(object sender, RoutedEventArgs e)
        {
            if (!RequireOpenDoc()) return;

            // Use Highlight annotations as redaction regions, mapped from render pixels to PDF points.
            var rects = new List<RedactRect>();
            foreach (var kvp in _annotations)
            {
                int pageIdx = kvp.Key;
                if (pageIdx >= _doc!.PageCount) continue;
                if (!_renderDims.TryGetValue(pageIdx, out var dims) || dims.w <= 0 || dims.h <= 0) continue;
                double sx = _doc.Pages[pageIdx].Width.Point / dims.w;
                double sy = _doc.Pages[pageIdx].Height.Point / dims.h;
                foreach (var ann in kvp.Value)
                    if (ann is HighlightAnnotation ha)
                        rects.Add(new RedactRect
                        {
                            PageIndex = pageIdx,
                            XPt = ha.Bounds.X * sx, YPt = ha.Bounds.Y * sy,
                            WidthPt = ha.Bounds.Width * sx, HeightPt = ha.Bounds.Height * sy,
                        });
            }

            if (rects.Count == 0)
            {
                ScalpelDialog.Show(this,
                    "To redact, first mark the areas to remove with the Highlight tool (Edit mode), then run this again.\n\nEach marked area's page is permanently flattened to an image with a black box, so the hidden text cannot be recovered.",
                    "Redact", MessageBoxButton.OK, MessageBoxImage.None);
                return;
            }

            if (ScalpelDialog.Show(this,
                    $"Permanently redact {rects.Count} marked area(s)?\n\nThe affected pages become flattened images — the underlying text is removed and cannot be recovered.",
                    "Confirm Redaction", MessageBoxButton.OKCancel, MessageBoxImage.None) != MessageBoxResult.OK)
                return;

            // Consume the highlight markers so they don't also burn in as colored boxes.
            foreach (var list in _annotations.Values)
                list.RemoveAll(a => a is HighlightAnnotation);

            // Everything below — building the working copy, the native pdfium rasterization, and
            // adopting the result — is inside one try so ANY managed failure surfaces as a dialog
            // instead of an unhandled exception (which would crash the app). NOTE: a true native
            // AccessViolation inside pdfium still can't be caught here (see App.xaml.cs crash notes),
            // but capping the render size keeps memory bounded so we don't provoke one.
            string outPath = App.MakeTempFile("redacted");
            SetStatus("Redacting…");
            try
            {
                string src = BuildWorkingSourceFile();
                await Task.Run(() =>
                {
                    using var rasterizer = new DocnetPageRasterizer(src, 2200);
                    RedactionService.Redact(src, rasterizer, rects, outPath);
                });
                AdoptTransformedFile(outPath,
                    $"Redacted {rects.Count} area(s) — affected pages are now flattened images.");
            }
            catch (Exception ex)
            {
                Scalpel.Services.Logger.Error("Tools", "redact.fail", ex.Message, ex);
                SetStatus("Redaction failed");
                ScalpelDialog.Show(this, $"Redaction failed:\n{ex.Message}", "Scalpel",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ---- generic run helper -----------------------------------------------------------------

        private void RunFileTransform(string busyStatus, Func<string, string> transform, string doneStatus)
        {
            try
            {
                SetStatus(busyStatus);
                string src = BuildWorkingSourceFile();
                string outPath = transform(src);
                AdoptTransformedFile(outPath, doneStatus);
            }
            catch (Exception ex)
            {
                ScalpelDialog.Show(this, $"Operation failed:\n{ex.Message}", "Scalpel",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static int ParseInt(string? s, int fallback)
            => int.TryParse(s, out int v) ? v : fallback;

        private static long SafeLen(string path)
        {
            try { return new FileInfo(path).Length; } catch { return 0; }
        }

        private static string FmtSize(long bytes)
        {
            if (bytes >= 1024 * 1024) return $"{bytes / (1024.0 * 1024.0):0.0} MB";
            if (bytes >= 1024) return $"{bytes / 1024.0:0} KB";
            return $"{bytes} B";
        }

        private static string Pct(long before, long after)
        {
            if (before <= 0) return "";
            double saved = 100.0 * (before - after) / before;
            return saved >= 0 ? $"-{saved:0}%" : $"+{-saved:0}%";
        }
    }

    // ---- minimal themed form -------------------------------------------------------------------

    internal enum ToolFieldKind { Text, Int, Password, Combo, Check }

    internal sealed class ToolField
    {
        public ToolField(string label, ToolFieldKind kind, string? value = null,
            string[]? options = null, bool isChecked = false)
        {
            Label = label; Kind = kind; Value = value ?? ""; Options = options; Checked = isChecked;
        }
        public string Label { get; }
        public ToolFieldKind Kind { get; }
        public string Value { get; set; }
        public string[]? Options { get; }
        public bool Checked { get; set; }
    }
}

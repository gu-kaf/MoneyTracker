// DocxWriter.cs —— 零第三方依赖的 .docx 链式构建器
//
// 只用 .NET 内置库：
//   System.IO.Compression.ZipArchive  负责 OOXML 的 zip 打包
//   System.Text.StringBuilder         负责拼 XML
// 不引用任何 System.Windows / WPF 类型，可被控制台程序或 WPF 程序共用。
//
// 生成的包结构（ECMA-376 WordprocessingML，最小可用集）：
//   [Content_Types].xml
//   _rels/.rels
//   word/document.xml
//   word/_rels/document.xml.rels
//   word/styles.xml
//
// 用法：
//   var doc = new DocxBuilder();
//   doc.Title("记账报告");
//   doc.KeyValue("统计区间", "2024-01-01 ~ 2024-01-31");
//   doc.Table(new[]{"日期","金额"}, rows);
//   doc.Save(@"D:\out.docx");

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace MoneyTracker
{
    /// <summary>链式构建 docx。</summary>
    public class DocxBuilder
    {
        // ---- OOXML 命名空间 ----
        private const string NsWord = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        private const string NsRel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        private const string NsPkgRel = "http://schemas.openxmlformats.org/package/2006/relationships";
        private const string NsContentTypes = "http://schemas.openxmlformats.org/package/2006/content-types";

        private const string CtDocument = "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml";
        private const string CtStyles = "application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml";
        private const string CtSettings = "application/vnd.openxmlformats-officedocument.wordprocessingml.settings+xml";

        /// <summary>A4 纵向页宽 11906 twips 减去左右页边距各 1440，得到正文可用宽度。</summary>
        private const int UsableWidthTwips = 9026;

        private const int MinColumnTwips = 700;
        private const double MaxColumnWeight = 42.0;
        private const double MinColumnWeight = 4.0;

        private const string HeaderFill = "DCE6F1";

        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);
        private static readonly string[] NoCells = new string[0];

        private readonly StringBuilder _body = new StringBuilder();
        private bool _hasBlock;
        private bool _lastIsTable;

        // ------------------------------------------------------------------
        // 链式 API
        // ------------------------------------------------------------------

        /// <summary>文档大标题（居中、加粗、大字）。</summary>
        public void Title(string text)
        {
            AppendParagraph("DocTitle", text);
        }

        /// <summary>小标题。</summary>
        public void Heading(string text)
        {
            AppendParagraph("DocHeading", text);
        }

        /// <summary>正文段落。</summary>
        public void Paragraph(string text)
        {
            AppendParagraph(null, text);
        }

        /// <summary>灰色小字。</summary>
        public void Muted(string text)
        {
            AppendParagraph("DocMuted", text);
        }

        /// <summary>一行「左标签 右值」，右值靠右对齐（用右制表位）。</summary>
        public void KeyValue(string key, string value)
        {
            _body.Append("<w:p><w:pPr>");
            _body.Append("<w:tabs><w:tab w:val=\"right\" w:pos=\"").Append(UsableWidthTwips.ToString(CultureInfo.InvariantCulture)).Append("\"/></w:tabs>");
            _body.Append("<w:spacing w:before=\"0\" w:after=\"80\"/>");
            _body.Append("</w:pPr>");
            AppendRun(_body, key, true, null, null);
            _body.Append("<w:r><w:tab/></w:r>");
            AppendRun(_body, value, false, null, null);
            _body.Append("</w:p>");
            MarkBlock(false);
        }

        /// <summary>表格（表头带底纹、跨页自动重复表头）。</summary>
        public void Table(string[] headers, List<string[]> rows)
        {
            string[] head = headers ?? NoCells;
            List<string[]> body = rows ?? new List<string[]>();

            int columnCount = head.Length;
            for (int i = 0; i < body.Count; i++)
            {
                string[] row = body[i];
                if (row != null && row.Length > columnCount) columnCount = row.Length;
            }
            if (columnCount <= 0) return;      // 没有表头也没有数据，不画空表

            int[] widths = BuildColumnWidths(columnCount, head, body);

            _body.Append("<w:tbl><w:tblPr>");
            _body.Append("<w:tblStyle w:val=\"TableGrid\"/>");
            _body.Append("<w:tblW w:w=\"").Append(UsableWidthTwips.ToString(CultureInfo.InvariantCulture)).Append("\" w:type=\"dxa\"/>");
            _body.Append("<w:jc w:val=\"center\"/>");
            _body.Append("<w:tblBorders>");
            _body.Append("<w:top w:val=\"single\" w:sz=\"4\" w:space=\"0\" w:color=\"BFBFBF\"/>");
            _body.Append("<w:left w:val=\"single\" w:sz=\"4\" w:space=\"0\" w:color=\"BFBFBF\"/>");
            _body.Append("<w:bottom w:val=\"single\" w:sz=\"4\" w:space=\"0\" w:color=\"BFBFBF\"/>");
            _body.Append("<w:right w:val=\"single\" w:sz=\"4\" w:space=\"0\" w:color=\"BFBFBF\"/>");
            _body.Append("<w:insideH w:val=\"single\" w:sz=\"4\" w:space=\"0\" w:color=\"BFBFBF\"/>");
            _body.Append("<w:insideV w:val=\"single\" w:sz=\"4\" w:space=\"0\" w:color=\"BFBFBF\"/>");
            _body.Append("</w:tblBorders>");
            _body.Append("<w:tblLayout w:type=\"fixed\"/>");
            _body.Append("<w:tblLook w:val=\"04A0\" w:firstRow=\"1\" w:lastRow=\"0\" w:firstColumn=\"0\" w:lastColumn=\"0\" w:noHBand=\"0\" w:noVBand=\"1\"/>");
            _body.Append("</w:tblPr>");

            _body.Append("<w:tblGrid>");
            for (int c = 0; c < columnCount; c++)
                _body.Append("<w:gridCol w:w=\"").Append(widths[c].ToString(CultureInfo.InvariantCulture)).Append("\"/>");
            _body.Append("</w:tblGrid>");

            if (head.Length > 0)
            {
                _body.Append("<w:tr><w:trPr><w:tblHeader/></w:trPr>");
                for (int c = 0; c < columnCount; c++)
                    AppendCell(c < head.Length ? head[c] : string.Empty, widths[c], HeaderFill, true);
                _body.Append("</w:tr>");
            }

            for (int i = 0; i < body.Count; i++)
            {
                string[] row = body[i] ?? NoCells;
                _body.Append("<w:tr>");
                for (int c = 0; c < columnCount; c++)
                    AppendCell(c < row.Length ? row[c] : string.Empty, widths[c], null, false);
                _body.Append("</w:tr>");
            }

            _body.Append("</w:tbl>");
            MarkBlock(true);
        }

        /// <summary>分页符。</summary>
        public void PageBreak()
        {
            _body.Append("<w:p><w:r><w:br w:type=\"page\"/></w:r></w:p>");
            MarkBlock(false);
        }

        /// <summary>写出 .docx。path 的上级目录不存在时会自动创建。</summary>
        public void Save(string path)
        {
            if (path == null || path.Trim().Length == 0)
                throw new ArgumentException("路径不能为空。", "path");

            StringBuilder finalBody = new StringBuilder(_body.Length + 64);
            finalBody.Append(_body);
            // 文档必须有内容；表格后必须跟一个段落，否则 Word 会提示修复
            if (!_hasBlock || _lastIsTable) finalBody.Append("<w:p/>");

            string document = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
                + "<w:document xmlns:w=\"" + NsWord + "\"><w:body>"
                + finalBody
                + BuildSectionProperties()
                + "</w:body></w:document>";

            string full = Path.GetFullPath(path);
            string dir = Path.GetDirectoryName(full);
            if (dir != null && dir.Length > 0 && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            using (FileStream fs = new FileStream(full, FileMode.Create, FileAccess.Write, FileShare.None))
            using (ZipArchive zip = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                AddEntry(zip, "[Content_Types].xml", BuildContentTypes());
                AddEntry(zip, "_rels/.rels", BuildRootRels());
                AddEntry(zip, "word/document.xml", document);
                AddEntry(zip, "word/_rels/document.xml.rels", BuildDocumentRels());
                AddEntry(zip, "word/styles.xml", BuildStyles());
                AddEntry(zip, "word/settings.xml", BuildSettings());
            }
        }

        // ------------------------------------------------------------------
        // 内部拼接
        // ------------------------------------------------------------------

        private void MarkBlock(bool isTable)
        {
            _hasBlock = true;
            _lastIsTable = isTable;
        }

        private void AppendParagraph(string styleId, string text)
        {
            if (string.IsNullOrEmpty(text) && styleId == null)
            {
                _body.Append("<w:p/>");
                MarkBlock(false);
                return;
            }

            _body.Append("<w:p>");
            if (styleId != null)
                _body.Append("<w:pPr><w:pStyle w:val=\"").Append(styleId).Append("\"/></w:pPr>");
            AppendRun(_body, text, false, null, null);
            _body.Append("</w:p>");
            MarkBlock(false);
        }

        private void AppendCell(string text, int widthTwips, string fill, bool bold)
        {
            _body.Append("<w:tc><w:tcPr>");
            _body.Append("<w:tcW w:w=\"").Append(widthTwips.ToString(CultureInfo.InvariantCulture)).Append("\" w:type=\"dxa\"/>");
            if (fill != null)
                _body.Append("<w:shd w:val=\"clear\" w:color=\"auto\" w:fill=\"").Append(fill).Append("\"/>");
            _body.Append("<w:vAlign w:val=\"center\"/>");
            _body.Append("</w:tcPr>");
            _body.Append("<w:p><w:pPr><w:spacing w:before=\"40\" w:after=\"40\"/></w:pPr>");
            AppendRun(_body, text, bold, null, null);
            _body.Append("</w:p></w:tc>");
        }

        /// <summary>写一个 run；文本里的换行会变成 &lt;w:br/&gt;。</summary>
        private static void AppendRun(StringBuilder sb, string text, bool bold, string color, int? halfPointSize)
        {
            sb.Append("<w:r>");
            if (bold || color != null || halfPointSize.HasValue)
            {
                sb.Append("<w:rPr>");
                if (bold) sb.Append("<w:b/>");
                if (color != null) sb.Append("<w:color w:val=\"").Append(color).Append("\"/>");
                if (halfPointSize.HasValue)
                {
                    string sz = halfPointSize.Value.ToString(CultureInfo.InvariantCulture);
                    sb.Append("<w:sz w:val=\"").Append(sz).Append("\"/><w:szCs w:val=\"").Append(sz).Append("\"/>");
                }
                sb.Append("</w:rPr>");
            }

            string normalized = (text ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');
            string[] lines = normalized.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                if (i > 0) sb.Append("<w:br/>");
                sb.Append("<w:t xml:space=\"preserve\">").Append(Escape(lines[i])).Append("</w:t>");
            }
            sb.Append("</w:r>");
        }

        /// <summary>按内容长度按比例分配列宽（twips），最后一列吸收舍入误差。</summary>
        private static int[] BuildColumnWidths(int columnCount, string[] headers, List<string[]> rows)
        {
            double[] weights = new double[columnCount];
            for (int c = 0; c < columnCount; c++)
            {
                double w = (c < headers.Length) ? DisplayWidth(headers[c]) : 0;
                for (int i = 0; i < rows.Count; i++)
                {
                    string[] row = rows[i];
                    if (row == null || c >= row.Length) continue;
                    double rw = DisplayWidth(row[c]);
                    if (rw > w) w = rw;
                }
                if (w > MaxColumnWeight) w = MaxColumnWeight;
                if (w < MinColumnWeight) w = MinColumnWeight;
                weights[c] = w;
            }

            double sum = 0;
            for (int c = 0; c < columnCount; c++) sum += weights[c];
            if (sum <= 0) sum = columnCount;

            int[] widths = new int[columnCount];
            int used = 0;
            for (int c = 0; c < columnCount; c++)
            {
                int v = (int)Math.Round(weights[c] / sum * UsableWidthTwips, MidpointRounding.AwayFromZero);
                if (v < MinColumnTwips) v = MinColumnTwips;
                widths[c] = v;
                used += v;
            }

            int diff = UsableWidthTwips - used;
            widths[columnCount - 1] += diff;
            if (widths[columnCount - 1] < MinColumnTwips) widths[columnCount - 1] = MinColumnTwips;
            return widths;
        }

        private static string BuildSectionProperties()
        {
            return "<w:sectPr>"
                + "<w:pgSz w:w=\"11906\" w:h=\"16838\"/>"
                + "<w:pgMar w:top=\"1440\" w:right=\"1440\" w:bottom=\"1440\" w:left=\"1440\" w:header=\"851\" w:footer=\"992\" w:gutter=\"0\"/>"
                + "<w:cols w:space=\"425\"/>"
                + "<w:docGrid w:linePitch=\"312\"/>"
                + "</w:sectPr>";
        }

        // ------------------------------------------------------------------
        // 包部件
        // ------------------------------------------------------------------

        private static string BuildContentTypes()
        {
            return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
                + "<Types xmlns=\"" + NsContentTypes + "\">"
                + "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>"
                + "<Default Extension=\"xml\" ContentType=\"application/xml\"/>"
                + "<Override PartName=\"/word/document.xml\" ContentType=\"" + CtDocument + "\"/>"
                + "<Override PartName=\"/word/styles.xml\" ContentType=\"" + CtStyles + "\"/>"
                + "<Override PartName=\"/word/settings.xml\" ContentType=\"" + CtSettings + "\"/>"
                + "</Types>";
        }

        private static string BuildRootRels()
        {
            return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
                + "<Relationships xmlns=\"" + NsPkgRel + "\">"
                + "<Relationship Id=\"rId1\" Type=\"" + NsRel + "/officeDocument\" Target=\"word/document.xml\"/>"
                + "</Relationships>";
        }

        private static string BuildDocumentRels()
        {
            return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
                + "<Relationships xmlns=\"" + NsPkgRel + "\">"
                + "<Relationship Id=\"rId1\" Type=\"" + NsRel + "/styles\" Target=\"styles.xml\"/>"
                + "<Relationship Id=\"rId2\" Type=\"" + NsRel + "/settings\" Target=\"settings.xml\"/>"
                + "</Relationships>";
        }

        /// <summary>
        /// 这个部件只为一件事存在：声明 compatibilityMode=15，
        /// 不然 Word 打开的时候标题栏会挂一个「[兼容模式]」，看着像旧文件。
        /// </summary>
        private static string BuildSettings()
        {
            return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
                + "<w:settings xmlns:w=\"" + NsWord + "\">"
                + "<w:zoom w:percent=\"100\"/>"
                + "<w:defaultTabStop w:val=\"420\"/>"
                + "<w:compat>"
                + "<w:compatSetting w:name=\"compatibilityMode\" w:uri=\"http://schemas.microsoft.com/office/word\" w:val=\"15\"/>"
                + "</w:compat>"
                + "</w:settings>";
        }

        /// <summary>最小样式表：默认字体/段落、DocTitle、DocHeading、DocMuted、TableGrid。</summary>
        private static string BuildStyles()
        {
            // \u7b49\u7ebf = 等线（中文正文常用）；\u5e38\u89c4 = 常规
            const string eastAsiaFont = "\u7b49\u7ebf";

            StringBuilder sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
            sb.Append("<w:styles xmlns:w=\"").Append(NsWord).Append("\">");

            sb.Append("<w:docDefaults>");
            sb.Append("<w:rPrDefault><w:rPr>");
            sb.Append("<w:rFonts w:ascii=\"Calibri\" w:hAnsi=\"Calibri\" w:eastAsia=\"").Append(eastAsiaFont).Append("\" w:cs=\"Calibri\"/>");
            sb.Append("<w:sz w:val=\"21\"/><w:szCs w:val=\"21\"/>");
            sb.Append("<w:lang w:val=\"en-US\" w:eastAsia=\"zh-CN\"/>");
            sb.Append("</w:rPr></w:rPrDefault>");
            sb.Append("<w:pPrDefault><w:pPr>");
            sb.Append("<w:spacing w:after=\"120\" w:line=\"300\" w:lineRule=\"auto\"/>");
            sb.Append("</w:pPr></w:pPrDefault>");
            sb.Append("</w:docDefaults>");

            sb.Append("<w:style w:type=\"paragraph\" w:default=\"1\" w:styleId=\"Normal\">");
            sb.Append("<w:name w:val=\"Normal\"/><w:qFormat/>");
            sb.Append("</w:style>");

            sb.Append("<w:style w:type=\"paragraph\" w:styleId=\"DocTitle\">");
            sb.Append("<w:name w:val=\"Doc Title\"/><w:basedOn w:val=\"Normal\"/><w:next w:val=\"Normal\"/><w:qFormat/>");
            sb.Append("<w:pPr><w:spacing w:before=\"0\" w:after=\"240\"/><w:jc w:val=\"center\"/></w:pPr>");
            sb.Append("<w:rPr><w:b/><w:color w:val=\"1F3864\"/><w:sz w:val=\"44\"/><w:szCs w:val=\"44\"/></w:rPr>");
            sb.Append("</w:style>");

            sb.Append("<w:style w:type=\"paragraph\" w:styleId=\"DocHeading\">");
            sb.Append("<w:name w:val=\"Doc Heading\"/><w:basedOn w:val=\"Normal\"/><w:next w:val=\"Normal\"/><w:qFormat/>");
            sb.Append("<w:pPr><w:keepNext/><w:spacing w:before=\"240\" w:after=\"120\"/><w:outlineLvl w:val=\"1\"/></w:pPr>");
            sb.Append("<w:rPr><w:b/><w:color w:val=\"2E74B5\"/><w:sz w:val=\"28\"/><w:szCs w:val=\"28\"/></w:rPr>");
            sb.Append("</w:style>");

            sb.Append("<w:style w:type=\"paragraph\" w:styleId=\"DocMuted\">");
            sb.Append("<w:name w:val=\"Doc Muted\"/><w:basedOn w:val=\"Normal\"/><w:qFormat/>");
            sb.Append("<w:pPr><w:spacing w:before=\"0\" w:after=\"80\"/></w:pPr>");
            sb.Append("<w:rPr><w:color w:val=\"808080\"/><w:sz w:val=\"18\"/><w:szCs w:val=\"18\"/></w:rPr>");
            sb.Append("</w:style>");

            sb.Append("<w:style w:type=\"table\" w:default=\"1\" w:styleId=\"TableNormal\">");
            sb.Append("<w:name w:val=\"Normal Table\"/>");
            sb.Append("<w:tblPr><w:tblInd w:w=\"0\" w:type=\"dxa\"/>");
            sb.Append("<w:tblCellMar><w:top w:w=\"0\" w:type=\"dxa\"/><w:left w:w=\"108\" w:type=\"dxa\"/>");
            sb.Append("<w:bottom w:w=\"0\" w:type=\"dxa\"/><w:right w:w=\"108\" w:type=\"dxa\"/></w:tblCellMar>");
            sb.Append("</w:tblPr></w:style>");

            sb.Append("<w:style w:type=\"table\" w:styleId=\"TableGrid\">");
            sb.Append("<w:name w:val=\"Table Grid\"/><w:basedOn w:val=\"TableNormal\"/>");
            sb.Append("<w:tblPr><w:tblBorders>");
            sb.Append("<w:top w:val=\"single\" w:sz=\"4\" w:space=\"0\" w:color=\"BFBFBF\"/>");
            sb.Append("<w:left w:val=\"single\" w:sz=\"4\" w:space=\"0\" w:color=\"BFBFBF\"/>");
            sb.Append("<w:bottom w:val=\"single\" w:sz=\"4\" w:space=\"0\" w:color=\"BFBFBF\"/>");
            sb.Append("<w:right w:val=\"single\" w:sz=\"4\" w:space=\"0\" w:color=\"BFBFBF\"/>");
            sb.Append("<w:insideH w:val=\"single\" w:sz=\"4\" w:space=\"0\" w:color=\"BFBFBF\"/>");
            sb.Append("<w:insideV w:val=\"single\" w:sz=\"4\" w:space=\"0\" w:color=\"BFBFBF\"/>");
            sb.Append("</w:tblBorders></w:tblPr>");
            sb.Append("<w:tblStylePr w:type=\"firstRow\"><w:rPr><w:b/></w:rPr></w:tblStylePr>");
            sb.Append("</w:style>");

            sb.Append("</w:styles>");
            return sb.ToString();
        }

        // ------------------------------------------------------------------
        // 文本宽度 / XML 转义 / zip
        // ------------------------------------------------------------------

        /// <summary>粗略的显示宽度：CJK / 全角按 2 个字符宽算。</summary>
        private static double DisplayWidth(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            double w = 0;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '\n' || c == '\r') { w += 2; continue; }
                w += IsWide(c) ? 2.0 : 1.0;
            }
            return w;
        }

        private static bool IsWide(char c)
        {
            return (c >= 0x1100 && c <= 0x115F)
                || (c >= 0x2E80 && c <= 0x303E)
                || (c >= 0x3041 && c <= 0x33FF)
                || (c >= 0x3400 && c <= 0x4DBF)
                || (c >= 0x4E00 && c <= 0x9FFF)
                || (c >= 0xA000 && c <= 0xA4CF)
                || (c >= 0xAC00 && c <= 0xD7A3)
                || (c >= 0xF900 && c <= 0xFAFF)
                || (c >= 0xFE30 && c <= 0xFE6F)
                || (c >= 0xFF00 && c <= 0xFF60)
                || (c >= 0xFFE0 && c <= 0xFFE6);
        }

        /// <summary>XML 转义（&amp; &lt; &gt; &quot; &apos;），并剔除 XML 1.0 非法字符。</summary>
        private static string Escape(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;

            string s = StripInvalidXmlChars(text);
            StringBuilder sb = new StringBuilder(s.Length + 16);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                switch (c)
                {
                    case '&': sb.Append("&amp;"); continue;
                    case '<': sb.Append("&lt;"); continue;
                    case '>': sb.Append("&gt;"); continue;
                    case '"': sb.Append("&quot;"); continue;
                    case '\'': sb.Append("&apos;"); continue;
                    default: sb.Append(c); continue;
                }
            }
            return sb.ToString();
        }

        /// <summary>去掉 XML 1.0 不允许的字符，并把落单的代理项换成 U+FFFD，否则文件会被判定为损坏。</summary>
        private static string StripInvalidXmlChars(string text)
        {
            StringBuilder sb = null;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                bool ok;
                int len = 1;

                if (c == '\t' || c == '\n' || c == '\r')
                {
                    ok = true;
                }
                else if (char.IsHighSurrogate(c))
                {
                    ok = i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]);
                    if (ok) len = 2;
                }
                else if (char.IsLowSurrogate(c))
                {
                    ok = false;
                }
                else
                {
                    ok = c >= 0x20 && c != 0xFFFE && c != 0xFFFF;
                }

                if (ok)
                {
                    if (sb != null) sb.Append(text, i, len);
                    if (len == 2) i++;
                }
                else
                {
                    if (sb == null)
                    {
                        sb = new StringBuilder(text.Length + 8);
                        sb.Append(text, 0, i);
                    }
                    sb.Append('\ufffd');
                }
            }
            return sb == null ? text : sb.ToString();
        }

        private static void AddEntry(ZipArchive zip, string entryName, string content)
        {
            ZipArchiveEntry entry = zip.CreateEntry(entryName, CompressionLevel.Optimal);
            byte[] bytes = Utf8NoBom.GetBytes(content);
            using (Stream stream = entry.Open())
            {
                stream.Write(bytes, 0, bytes.Length);
            }
        }
    }
}
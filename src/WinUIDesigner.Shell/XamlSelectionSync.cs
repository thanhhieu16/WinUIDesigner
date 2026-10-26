using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using Microsoft.UI.Xaml;
using WinUIDesigner.DesignModel;
using WinUIDesigner.DesignModel.Xaml;
using WinUIDesigner.XamlDom;

namespace WinUIDesigner.Shell
{
    /// <summary>
    /// Carries the selection across a mode switch: the selected item's start tag in the XAML
    /// editor on the way in, the element under the caret on the way back to Design.
    /// </summary>
    /// <remarks>
    /// An element is addressed by its path of element-child indices from the document element,
    /// not by line and column. Line info on the parsed tree describes the text it was parsed
    /// from, and the editor may hold a re-serialization of that tree instead (Document.Mode
    /// re-serializes a document that has moved on), which re-indents and re-sorts attributes but
    /// writes exactly the elements the XmlDocument holds. So the path of an element in the
    /// XmlDocument is its path in the editor's text, whichever of the two the editor shows.
    /// </remarks>
    internal static class XamlSelectionSync
    {
        /// <summary>
        /// The element-index path of <paramref name="element"/> below its document element, or
        /// null when it is not attached to a document (a virtual element backing an attribute).
        /// </summary>
        internal static List<int> PathOf(XmlElement element)
        {
            if (element == null || element.OwnerDocument == null)
                return null;

            var path = new List<int>();
            var current = element;
            while (current != element.OwnerDocument.DocumentElement)
            {
                var parent = current.ParentNode as XmlElement;
                if (parent == null)
                    return null;
                int index = 0;
                for (var sibling = parent.FirstChild; sibling != current; sibling = sibling.NextSibling)
                {
                    if (sibling is XmlElement)
                        index++;
                }
                path.Insert(0, index);
                current = parent;
            }
            return path;
        }

        /// <summary>The element at <paramref name="path"/> below <paramref name="root"/>, or null.</summary>
        internal static XmlElement ElementAt(XmlElement root, IList<int> path)
        {
            var current = root;
            foreach (int index in path)
            {
                XmlElement next = null;
                int n = 0;
                for (var child = current.FirstChild; child != null; child = child.NextSibling)
                {
                    if (child is XmlElement && n++ == index)
                    {
                        next = (XmlElement)child;
                        break;
                    }
                }
                if (next == null)
                    return null;
                current = next;
            }
            return current;
        }

        /// <summary>
        /// The path of the innermost element whose tags enclose <paramref name="offset"/> in
        /// <paramref name="text"/>, or null when the offset is outside the document element or
        /// the text is not well-formed up to it. A caret anywhere in a start or end tag counts as
        /// inside that element.
        /// </summary>
        internal static List<int> PathAt(string text, int offset)
        {
            List<int> found = null;
            Walk(text, (path, start, startTagEnd, isEmpty) =>
            {
                if (start > offset)
                    return WalkAction.Stop;
                if (isEmpty)
                {
                    if (offset <= startTagEnd)
                    {
                        found = path;
                        return WalkAction.Stop;
                    }
                    return WalkAction.Continue;
                }
                found = path;
                return WalkAction.Continue;
            }, (path, endTagEnd) =>
            {
                if (offset <= endTagEnd)
                    return WalkAction.Stop;
                found = path.Count == 0 ? null : path.GetRange(0, path.Count - 1);
                return WalkAction.Continue;
            });
            return found;
        }

        /// <summary>
        /// The start offset and length of the start tag of the element at <paramref name="path"/>
        /// in <paramref name="text"/>, or null when there is no such element.
        /// </summary>
        internal static Tuple<int, int> StartTagOf(string text, IList<int> path)
        {
            Tuple<int, int> found = null;
            Walk(text, (p, start, startTagEnd, isEmpty) =>
            {
                if (SamePath(p, path))
                {
                    found = Tuple.Create(start, startTagEnd - start);
                    return WalkAction.Stop;
                }
                return WalkAction.Continue;
            }, (p, endTagEnd) => WalkAction.Continue);
            return found;
        }

        /// <summary>
        /// The design item whose element is at <paramref name="path"/>, or the nearest one above
        /// it that the surface can select: a property element such as &lt;Grid.RowDefinitions&gt;
        /// and a non-visual object such as a RowDefinition both resolve to their Grid.
        /// </summary>
        internal static DesignItem ItemAt(DesignContext context, IList<int> path)
        {
            var root = context == null ? null : context.RootItem as XamlDesignItem;
            if (root == null || path == null)
                return null;

            var objects = new Dictionary<XmlNode, XamlObject>();
            Index(root.XamlObject, objects);

            XmlNode element = ElementAt(root.XamlObject.XmlElement, path);
            while (element != null)
            {
                XamlObject xamlObject;
                if (objects.TryGetValue(element, out xamlObject))
                {
                    var item = context.Services.Component.GetDesignItem(xamlObject.Instance);
                    if (item != null && item.Component is UIElement)
                        return item;
                }
                element = element.ParentNode as XmlElement;
            }
            return null;
        }

        static void Index(XamlObject xamlObject, Dictionary<XmlNode, XamlObject> objects)
        {
            if (xamlObject.XmlElement != null && !objects.ContainsKey(xamlObject.XmlElement))
                objects.Add(xamlObject.XmlElement, xamlObject);

            foreach (var property in xamlObject.Properties)
            {
                var single = property.PropertyValue as XamlObject;
                if (single != null)
                    Index(single, objects);
                if (property.IsCollection)
                {
                    foreach (var element in property.CollectionElements)
                    {
                        var child = element as XamlObject;
                        if (child != null)
                            Index(child, objects);
                    }
                }
            }
        }

        enum WalkAction { Continue, Stop }

        delegate WalkAction StartHandler(List<int> path, int start, int startTagEnd, bool isEmpty);
        delegate WalkAction EndHandler(List<int> path, int endTagEnd);

        /// <summary>
        /// Reads <paramref name="text"/> element by element, reporting each start tag's span and
        /// each end tag's end as offsets into <paramref name="text"/> itself. Offsets come from
        /// the reader's line info through DocumentView.OffsetOf, which counts CR, LF and CRLF as
        /// one break each - the same as XmlReader, and the editor holds lone CR (measured).
        /// Stops quietly at the first point the text is not well-formed.
        /// </summary>
        static void Walk(string text, StartHandler onStart, EndHandler onEnd)
        {
            if (string.IsNullOrEmpty(text))
                return;

            // One counter per open element, counting its element children so far. The bottom
            // entry counts document elements, of which there is one.
            var counts = new List<int> { 0 };
            var path = new List<int>();
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore };

            try
            {
                using (var reader = XmlReader.Create(new StringReader(text), settings))
                {
                    var info = (IXmlLineInfo)reader;
                    while (reader.Read())
                    {
                        if (reader.NodeType == XmlNodeType.Element)
                        {
                            int index = counts[counts.Count - 1]++;
                            List<int> elementPath;
                            if (counts.Count == 1)
                            {
                                elementPath = new List<int>();
                            }
                            else
                            {
                                elementPath = new List<int>(path);
                                elementPath.Add(index);
                            }

                            // LinePosition addresses the name, one past the '<'.
                            int start = DocumentView.OffsetOf(text, info.LineNumber, info.LinePosition) - 1;
                            if (start < 0)
                                return;
                            int startTagEnd = TagEnd(text, start);
                            bool isEmpty = reader.IsEmptyElement;

                            if (onStart(elementPath, start, startTagEnd, isEmpty) == WalkAction.Stop)
                                return;

                            if (!isEmpty)
                            {
                                path = elementPath;
                                counts.Add(0);
                            }
                        }
                        else if (reader.NodeType == XmlNodeType.EndElement)
                        {
                            // LinePosition addresses the name, two past the '<' of "</".
                            int start = DocumentView.OffsetOf(text, info.LineNumber, info.LinePosition) - 2;
                            if (start < 0)
                                return;
                            if (onEnd(path, TagEnd(text, start)) == WalkAction.Stop)
                                return;

                            counts.RemoveAt(counts.Count - 1);
                            path = path.Count == 0 ? path : path.GetRange(0, path.Count - 1);
                        }
                    }
                }
            }
            catch (XmlException)
            {
                // Not well-formed past this point; what was reported so far stands.
            }
        }

        /// <summary>
        /// The offset just past the '&gt;' that closes the tag starting at
        /// <paramref name="start"/>, skipping any '&gt;' inside a quoted attribute value.
        /// </summary>
        internal static int TagEnd(string text, int start)
        {
            char quote = '\0';
            for (int i = start; i < text.Length; i++)
            {
                char c = text[i];
                if (quote != '\0')
                {
                    if (c == quote)
                        quote = '\0';
                }
                else if (c == '"' || c == '\'')
                {
                    quote = c;
                }
                else if (c == '>')
                {
                    return i + 1;
                }
            }
            return text.Length;
        }

        static bool SamePath(IList<int> a, IList<int> b)
        {
            if (a.Count != b.Count)
                return false;
            for (int i = 0; i < a.Count; i++)
            {
                if (a[i] != b[i])
                    return false;
            }
            return true;
        }
    }
}

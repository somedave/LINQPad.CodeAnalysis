﻿using BrightIdeasSoftware;
using Microsoft.CodeAnalysis;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Reflection;
using Microsoft.Msagl.Drawing;
using Microsoft.Msagl.GraphViewerGdi;
using Color = System.Drawing.Color;
using Graph = Microsoft.Msagl.Drawing.Graph;
using Orientation = System.Windows.Forms.Orientation;

namespace LINQPad.CodeAnalysis
{
    internal class SyntaxTreePanel : TableLayoutPanel
    {
        public SyntaxTreePanel(SyntaxTree syntaxTree, string declarationFilter)
        {
            // Controls
            TextBox textBox = CreateTextBox();
            GViewer graphViewer = CreateGraphViewer();
            TreeListView treeList = CreateTreeList(textBox, graphViewer, syntaxTree, declarationFilter);
            ToolStrip toolStrip = CreateToolStrip(treeList, textBox, graphViewer, syntaxTree, declarationFilter);

            // Right-hand splitter
            SplitContainer rightSplitContainer = new SplitContainer()
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Horizontal
            };
            rightSplitContainer.Panel1.Controls.Add(textBox);
            rightSplitContainer.Panel2.Controls.Add(graphViewer);
            bool rightSplitVisibleChanged = false;
            rightSplitContainer.VisibleChanged += (x, e) =>
            {
                if (!rightSplitVisibleChanged)
                {
                    rightSplitVisibleChanged = true;
                    rightSplitContainer.SplitterDistance = (int)(rightSplitContainer.ClientSize.Height * 0.5);
                }
            };

            // Top-level splitter
            SplitContainer splitContainer = new SplitContainer()
            {
                Dock = DockStyle.Fill
            };
            splitContainer.Panel1.Controls.Add(treeList);
            splitContainer.Panel2.Controls.Add(rightSplitContainer);
            bool splitVisibleChanged = false;
            splitContainer.VisibleChanged += (x, e) =>
            {
                if (!splitVisibleChanged)
                {
                    splitVisibleChanged = true;
                    splitContainer.SplitterDistance = (int)(splitContainer.ClientSize.Width * 0.5);
                }
            };
            Controls.Add(toolStrip, 0, 0);
            Controls.Add(splitContainer, 0, 1);
        }

        private static TextBox CreateTextBox()
        {
            TextBox textBox = new TextBox()
            {
                Multiline = true,
                Dock = DockStyle.Fill,
                Font = InternalsHelper.GetDefaultFont()
            };
            return textBox;
        }

        private static GViewer CreateGraphViewer()
        {
            GViewer graphViewer = new GViewer
            {
                Dock = DockStyle.Fill
            };
            return graphViewer;
        }

        private static TreeListView CreateTreeList(TextBox textBox, GViewer graphViewer, SyntaxTree syntaxTree, string declarationFilter)
        {
            // Create the tree view
            TreeListView treeList = new TreeListView()
            {
                CanExpandGetter = x => ((SyntaxWrapper)x).CanExpand(),
                ChildrenGetter = x => ((SyntaxWrapper)x).GetChildren(),
                UseCellFormatEvents = true,
                Dock = DockStyle.Fill,
                UseFiltering = true
            };
            treeList.FormatCell += (x, e) => ((SyntaxWrapper)e.CellValue).FormatCell(e);
            treeList.BeforeSorting += (x, e) => e.Canceled = true;

            // Select - show the text
            treeList.SelectedIndexChanged += (x, e) =>
            {
                if (treeList.SelectedItem != null)
                {
                    SyntaxWrapper wrapper = (SyntaxWrapper)treeList.SelectedItem.RowObject;
                    textBox.Text = wrapper.GetSyntaxObject().ToString();
                    PopulateGraph(graphViewer, wrapper);
                }
            };

            // Activate - dump the syntax object
            treeList.ItemActivate += (x, e) =>
            {
                if (treeList.SelectedItem != null)
                {
                    SyntaxWrapper wrapper = (SyntaxWrapper)treeList.SelectedItem.RowObject;
                    wrapper.GetSyntaxObject().Dump(wrapper.GetKind() + " " + wrapper.GetSpan());
                }
            };

            // Create columns
            treeList.Columns.Add(new OLVColumn("Kind", null)
            {
                AspectGetter = x => x,
                AspectToStringConverter = x => ((SyntaxWrapper)x).GetKind()
            });
            treeList.Columns.Add(new OLVColumn("Span", null)
            {
                AspectGetter = x => x,
                AspectToStringConverter = x => ((SyntaxWrapper)x).GetSpan()
            });
            treeList.Columns.Add(new OLVColumn("Text", null)
            {
                AspectGetter = x => x,
                AspectToStringConverter = x => ((SyntaxWrapper)x).GetText()
            });

            // Set the root
            SyntaxNode root;
            int depth = 0;
            if (syntaxTree.TryGetRoot(out root))
            {
                SetRoots(treeList, textBox, graphViewer, syntaxTree, declarationFilter);
                depth = GetDepth(root);
            }

            // Calculate control width
            AutoSizeColumns(treeList, depth, true);
            treeList.Layout += (x, e) => AutoSizeColumns(treeList, depth, false);

            return treeList;
        }

        private static ToolStrip CreateToolStrip(TreeListView treeList, TextBox textBox, GViewer graphViewer, SyntaxTree syntaxTree, string declarationFilter)
        {
            // Syntax and trivia toggles
            CheckBox syntaxTokenCheckBox = new CheckBox()
            {
                BackColor = Color.Transparent,
                Checked = true,
            };
            CheckBox syntaxTriviaCheckBox = new CheckBox()
            {
                BackColor = Color.Transparent,
                Checked = true,
            };
            bool handleChecked = true;	// Prevent double handling from adjustments during another handler
            syntaxTokenCheckBox.CheckedChanged += (x, e) =>
            {
                if (handleChecked)
                {
                    if (!syntaxTokenCheckBox.Checked)
                    {
                        handleChecked = false;
                        syntaxTriviaCheckBox.Checked = false;
                        handleChecked = true;
                    }
                    if (syntaxTokenCheckBox.Checked && syntaxTriviaCheckBox.Checked)
                    {
                        treeList.ModelFilter = null;
                    }
                    treeList.ModelFilter = new SyntaxFilter(syntaxTokenCheckBox.Checked, syntaxTriviaCheckBox.Checked);
                }
            };
            syntaxTriviaCheckBox.CheckedChanged += (x, e) =>
            {
                if (handleChecked)
                {
                    if (!syntaxTokenCheckBox.Checked)
                    {
                        handleChecked = false;
                        syntaxTokenCheckBox.Checked = true;
                        handleChecked = true;
                    }
                    if (syntaxTokenCheckBox.Checked && syntaxTriviaCheckBox.Checked)
                    {
                        treeList.ModelFilter = null;
                    }
                    treeList.ModelFilter = new SyntaxFilter(syntaxTokenCheckBox.Checked, syntaxTriviaCheckBox.Checked);
                }
            };

            // Declaration filter
            ToolStripTextBox declarationFilterTextBox = new ToolStripTextBox();
            if(!string.IsNullOrWhiteSpace(declarationFilter))
            {
                declarationFilterTextBox.Text = declarationFilter;
            }
            declarationFilterTextBox.KeyDown += (x, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    SetRoots(treeList, textBox, graphViewer, syntaxTree, declarationFilterTextBox.Text);
                }
            };
            declarationFilterTextBox.LostFocus += (x, e) =>
            {
                SetRoots(treeList, textBox, graphViewer, syntaxTree, declarationFilterTextBox.Text);
            };

            // Layout
            ToolStrip toolStrip = new ToolStrip(
                new ToolStripButton("Expand All", null, (x, e) => treeList.ExpandAll()),
                new ToolStripButton("Collapse All", null, (x, e) => treeList.CollapseAll()),
                new ToolStripSeparator(),
                new ToolStripLabel("SyntaxNode  ")
                {
                    ForeColor = Color.Blue
                },
                new ToolStripControlHost(syntaxTokenCheckBox),
                new ToolStripLabel("SyntaxToken  ")
                {
                    ForeColor = Color.Green
                },
                new ToolStripControlHost(syntaxTriviaCheckBox),
                new ToolStripLabel("SyntaxTrivia  ")
                {
                    ForeColor = Color.Maroon
                },
                new ToolStripLabel("(Double-Click A Node To Dump)"),
                new ToolStripSeparator(),
                new ToolStripLabel("Declaration Filter"),
                declarationFilterTextBox)
            {
                GripStyle = ToolStripGripStyle.Hidden,
                Renderer = new BorderlessToolStripRenderer(),
                Padding = new Padding(4)
            };
            toolStrip.Layout += (x, e) => toolStrip.Width = toolStrip.Parent.Width;

            return toolStrip;
        }

        public static void SetRoots(TreeListView treeList, TextBox textBox, GViewer graphViewer, SyntaxTree syntaxTree, string declarationFilter)
        {
            textBox.Text = string.Empty;
            PopulateGraph(graphViewer, null);
            SyntaxWrapper[] roots = new SyntaxTreeDeclarationFilter(declarationFilter)
                .GetMatchingSyntaxNodes(syntaxTree)
                .Select(x => SyntaxWrapper.Get(x))
                .ToArray();
            treeList.Roots = roots;
            if(roots.Length != 0)
            {
                treeList.ExpandAll();
                treeList.SelectedItem = treeList.GetItem(0);
                textBox.Text = roots[0].GetSyntaxObject().ToString();
                PopulateGraph(graphViewer, roots[0]);
            }
        }

        private class SyntaxFilter : IModelFilter
        {
            private readonly bool _tokens;
            private readonly bool _trivia;

            public SyntaxFilter(bool tokens, bool trivia)
            {
                _tokens = tokens;
                _trivia = trivia;
            }

            public bool Filter(object model)
            {
                SyntaxWrapper wrapper = (SyntaxWrapper)model;
                return !((wrapper.GetSyntaxObject() is SyntaxToken && !_tokens)
                    || (wrapper.GetSyntaxObject() is SyntaxTrivia && !_trivia));
            }
        }

        private class BorderlessToolStripRenderer : ToolStripProfessionalRenderer
        {
            protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
            {
                // Do nothing
            }
        }

        private static int GetDepth(SyntaxNodeOrToken syntax, int depth = 0)
        {
            if (syntax.IsNode)
            {
                return syntax.ChildNodesAndTokens().Count == 0 ? depth : syntax.ChildNodesAndTokens().Max(x => GetDepth(x, depth + 1));
            }
            return (syntax.AsToken().HasLeadingTrivia || syntax.AsToken().HasTrailingTrivia) ? depth + 1 : depth;
        }

        // From http://sourceforge.net/p/objectlistview/discussion/812922/thread/d2a643c1/?limit=25
        private static void AutoSizeColumns(TreeListView treeList, int depth, bool recalculate)
        {
            int totalWidth = 0;
            foreach (ColumnHeader col in treeList.Columns)
            {
                if (recalculate)
                {
                    col.AutoResize(ColumnHeaderAutoResizeStyle.ColumnContent);
                    col.Width += 16;
                    if (col.Index == 0)
                    {
                        // We have to manually take care of tree structure, checkbox and image
                        col.Width += 16 * depth;
                    }
                }
                if (col.Index == treeList.Columns.Count - 1)
                {
                    // Fill in the rest
                    col.Width = treeList.ClientSize.Width - totalWidth;
                }
                totalWidth += col.Width;
            }
        }

        // TODO: Filter for node types
        // TODO: Filter for declaration filter (this might already work due to set root)
        // TODO: Hide toolbar?
        // TODO: clicking a node highlights in the tree
        // TODO: better text values
        // TODO: white background
        private static void PopulateGraph(GViewer graphViewer, SyntaxWrapper wrapper)
        {
            // Setup
            graphViewer.SuspendLayout();
            Graph graph = new Graph();
            if (wrapper != null)
            {
                PopulateGraph(graph, wrapper, 0, null);
            }
            graphViewer.Graph = graph;
            graphViewer.ResumeLayout();
        }

        // Returns the last used id
        private static int PopulateGraph(Graph graph, SyntaxWrapper wrapper, int id, string parentId)
        {
            // Create the node
            string nodeId = id.ToString();
            Node node = new Node(nodeId);
            Color color = wrapper.GetColor();
            node.Attr.FillColor = new Microsoft.Msagl.Drawing.Color(color.R, color.G, color.B);
            node.LabelText = wrapper.GetKind();
            node.Label.FontColor = Microsoft.Msagl.Drawing.Color.White;
            graph.AddNode(node);

            // Add the edge
            if (parentId != null)
            {
                graph.AddEdge(parentId, nodeId);
            }

            // Descend
            IEnumerable children = wrapper.GetChildren();
            if (children != null)
            {
                // Note that we have to iterate children in reverse order to get them to layout in the correct order
                foreach (SyntaxWrapper childWrapper in children.Cast<SyntaxWrapper>().Reverse())
                {
                    id = PopulateGraph(graph, childWrapper, id + 1, nodeId);
                }
            }
            return id;
        }
    }
}

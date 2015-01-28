﻿using System;
using System.Drawing;
using System.Windows.Forms;
using Aga.Controls.Tree;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Aga.Controls.Tree.NodeControls;
using PluginCore.Managers;
using flash.tools.debugger;
using FlashDebugger.Controls.DataTree;
using PluginCore.Localization;
using PluginCore;
using flash.tools.debugger.expression;

namespace FlashDebugger.Controls
{
    public partial class DataTreeControl : UserControl, IToolTipProvider
    {
        public event EventHandler ValueChanged;

        private DataTreeModel _model;
        private static ViewerForm viewerForm;
        private ContextMenuStrip _contextMenuStrip;
        private ToolStripMenuItem copyMenuItem, viewerMenuItem, watchMenuItem;
        private List<String> expandedList = new List<String>();
        private bool watchMode;
        private bool addingNewExpression;

        public Collection<Node> Nodes
        {
            get
            {
                return _model.Root.Nodes;
            }
        }

        public TreeViewAdv Tree
        {
            get
            {
                return _tree;
            }
        }

        public ViewerForm Viewer
        {
            get
            {
                return viewerForm;
            }
        }

        public DataTreeControl():this(false)
        {
        }

        public DataTreeControl(bool watchMode)
        {
            InitializeComponent();
            ValueNodeTextBox.EditEnabled = true;
            ValueNodeTextBox.EditOnClick = true;
            ValueNodeTextBox.ToolTipProvider = this;

            if (watchMode)
            {
                this.watchMode = true;
                NameNodeTextBox.EditEnabled = true;
                NameNodeTextBox.DrawText += NameNodeTextBox_DrawText;
                NameNodeTextBox.EditorShowing += NameNodeTextBox_EditorShowing;
                NameNodeTextBox.EditorHided += NameNodeTextBox_EditorHided;
                NameNodeTextBox.LabelChanged += NameNodeTextBox_LabelChanged;
            }
            
            _model = new DataTreeModel();
            _tree.Model = _model;
            Controls.Add(_tree);
            _tree.Expanding += TreeExpanding;
            _tree.SelectionChanged += TreeSelectionChanged;
            _tree.LoadOnDemand = true;
            _tree.AutoRowHeight = true;
            ValueNodeTextBox.DrawText += ValueNodeTextBox_DrawText;
            ValueNodeTextBox.IsEditEnabledValueNeeded += ValueNodeTextBox_IsEditEnabledValueNeeded;
            ValueNodeTextBox.EditorShowing += ValueNodeTextBox_EditorShowing;
            ValueNodeTextBox.EditorHided += ValueNodeTextBox_EditorHided;
            ValueNodeTextBox.LabelChanged += ValueNodeTextBox_LabelChanged;
            _tree.NodeMouseDoubleClick += Tree_NodeMouseDoubleClick;
            _contextMenuStrip = new ContextMenuStrip();
            if (PluginBase.MainForm != null && PluginBase.Settings != null)
            {
                _contextMenuStrip.Font = PluginBase.Settings.DefaultFont;
                _contextMenuStrip.Renderer = new DockPanelStripRenderer(false);
            }
            _tree.ContextMenuStrip = _contextMenuStrip;
            this.NameTreeColumn.Header = TextHelper.GetString("Label.Name");
            this.ValueTreeColumn.Header = TextHelper.GetString("Label.Value");
            copyMenuItem = new ToolStripMenuItem(TextHelper.GetString("Label.Copy"), null, new EventHandler(this.CopyItemClick));
            viewerMenuItem = new ToolStripMenuItem(TextHelper.GetString("Label.Viewer"), null, new EventHandler(this.ViewerItemClick));
            _contextMenuStrip.Items.AddRange(new ToolStripMenuItem[] { copyMenuItem, viewerMenuItem});
            if (watchMode)
            {
                watchMenuItem = new ToolStripMenuItem(TextHelper.GetString("Label.Unwatch"), null, new EventHandler(this.WatchItemClick));
            }
            else
            {
                watchMenuItem = new ToolStripMenuItem(TextHelper.GetString("Label.Watch"), null, new EventHandler(this.WatchItemClick));
            }
            _contextMenuStrip.Items.Add(watchMenuItem);
            TreeSelectionChanged(null, null);
            viewerForm = new ViewerForm();
            viewerForm.StartPosition = FormStartPosition.Manual;
        }

        void NameNodeTextBox_DrawText(object sender, DrawEventArgs e)
        {
            try
            {
                if (e.Node.NextNode == null && !addingNewExpression)
                {
                    e.Font = new Font(e.Font, FontStyle.Italic);
                }
            }
            catch (Exception) { }
        }

        void NameNodeTextBox_EditorHided(object sender, EventArgs e)
        {
            if (addingNewExpression)
            {
                NodeTextBox box = sender as NodeTextBox;
                var node = box.Parent.CurrentNode.Tag as Node;
                if (node.Text.Trim() == "") node.Text = "Add new expression";
                addingNewExpression = false;
            }

            // We need to update the tree to avoid some draw problems
            Tree.FullUpdate();
        }

        void NameNodeTextBox_EditorShowing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            NodeTextBox box = sender as NodeTextBox;
            var node = box.Parent.CurrentNode.Tag as Node;
            if (box.Parent.CurrentNode.NextNode == null)
            {
                addingNewExpression = true;
                node.Text = "";
            }
            else
                addingNewExpression = false;
        }

        void NameNodeTextBox_LabelChanged(object sender, LabelEventArgs e)
        {
            NodeTextBox box = sender as NodeTextBox;
            if (box.Parent.CurrentNode == null) return;
            DataNode node = box.Parent.CurrentNode.Tag as DataNode;

            if (e.NewLabel.Trim() == "")
            {
                node.Text = e.OldLabel != "" ? e.OldLabel : "Add new expression";
                return;
            }

            bool newExp;
            if (node.NextNode == null)
                newExp = PanelsHelper.watchUI.AddElement(e.NewLabel);
            else
                newExp = PanelsHelper.watchUI.ReplaceElement(e.OldLabel, e.NewLabel);

            if (!newExp) node.Text = e.OldLabel;
        }

        void ValueNodeTextBox_LabelChanged(object sender, LabelEventArgs e)
        {
            NodeTextBox box = sender as NodeTextBox;
            if (box.Parent.CurrentNode == null) return;
            VariableNode node = box.Parent.CurrentNode.Tag as VariableNode;
            node.IsEditing = false;
            try
            {
                var debugManager = PluginMain.debugManager;
                var flashInterface = debugManager.FlashInterface;
                IASTBuilder b = new ASTBuilder(false);
                ValueExp exp = b.parse(new java.io.StringReader(node.GetVariablePath()));
                var ctx = new ExpressionContext(flashInterface.Session, flashInterface.GetFrames()[debugManager.CurrentFrame]);
                var obj = exp.evaluate(ctx);
                
                node.Variable = (Variable)obj;

                if (!watchMode)
                    PanelsHelper.watchUI.UpdateElements();

                if (ValueChanged != null)
                    ValueChanged(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                ErrorManager.ShowError(TextHelper.GetString("Error.Reevaluate"), ex);
            }
        }

        void ValueNodeTextBox_EditorShowing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            NodeTextBox box = sender as NodeTextBox;
            VariableNode node = box.Parent.CurrentNode.Tag as VariableNode;
            node.IsEditing = true;
        }

        void ValueNodeTextBox_EditorHided(object sender, EventArgs e)
        {
            // We need to update the tree to avoid some draw problems
            Tree.FullUpdate();
        }

        void ValueNodeTextBox_IsEditEnabledValueNeeded(object sender, NodeControlValueEventArgs e)
        {
            VariableNode node = e.Node.Tag as VariableNode;
            if (node == null || node.Variable == null)
            {
                e.Value = false;
                return;
            }
            int type = node.Variable.getValue().getType();
            // FDB only allows setting the value of scalar variables.
            // Strings can be null, however there is no way with FDB of discerning this case.
            if (node.Variable.isAttributeSet(VariableAttribute_.READ_ONLY) || type != VariableType_.BOOLEAN && type != VariableType_.NUMBER && 
                type != VariableType_.STRING)
            {
                e.Value = false;
            }
        }

        void ValueNodeTextBox_DrawText(object sender, DrawEventArgs e)
        {
            try
            {
                VariableNode node = e.Node.Tag as VariableNode;
                FlashInterface flashInterface = PluginMain.debugManager.FlashInterface;
                if (node != null && node.Variable != null && node.Variable.hasValueChanged(flashInterface.Session))
                {
                    e.TextColor = Color.Red;
                }
            }
            catch (NullReferenceException) { }
        }

        public DataNode AddNode(DataNode node)
        {
            _model.Root.Nodes.Add(node);
            RestoreExpanded();
            return node;
        }

        public DataNode GetNode(string fullpath)
        {
            DataNode node = _model.FindNode(fullpath) as DataNode;
            return node;
        }

        public string GetFullPath(DataNode node)
        {
            return _model.GetFullPath(node);
        }

        public void Clear()
        {
            if (Nodes.Count > 0) SaveExpanded();
            Nodes.Clear();
        }


        private void CopyItemClick(Object sender, System.EventArgs e)
        {
            if (Tree.SelectedNode != null)
            {
                DataNode node = Tree.SelectedNode.Tag as DataNode;
                Clipboard.SetText(string.Format("{0} = {1}",node.Text, node.Value));
            }  
        }
        private void ViewerItemClick(Object sender, System.EventArgs e)
        {
            if (Tree.SelectedNode != null)
            {
                if (viewerForm == null)
                {
                    viewerForm = new ViewerForm();
                    viewerForm.StartPosition = FormStartPosition.Manual;
                }
                DataNode node = Tree.SelectedNode.Tag as DataNode;
                viewerForm.Exp = node.Text;
                if (node is ValueNode)
                {
                    var vNode = (ValueNode)node;
                    // use IsEditing to get unfiltered value
                    bool ed = vNode.IsEditing;
                    vNode.IsEditing = true;
                    viewerForm.Value = node.Value;
                    vNode.IsEditing = ed;
                }
                else
                {
                    viewerForm.Value = node.Value;
                }
                Form mainform = (PluginBase.MainForm as Form);
                viewerForm.Left = mainform.Left + mainform.Width / 2 - viewerForm.Width / 2;
                viewerForm.Top = mainform.Top + mainform.Height / 2 - viewerForm.Height / 2;
                viewerForm.ShowDialog();
            }
        }
        private void WatchItemClick(Object sender, EventArgs e)
        {
            if (Tree.SelectedNode == null) return;
            DataNode node = Tree.SelectedNode.Tag as DataNode;
            if (watchMode)
            {
                PanelsHelper.watchUI.RemoveElement(Tree.SelectedNode.Row);
            }
            else
            {
                PanelsHelper.watchUI.AddElement(node.GetVariablePath());
            }
        }

        void TreeSelectionChanged(Object sender, EventArgs e)
        {
            foreach (ToolStripMenuItem item in _contextMenuStrip.Items)
            {
                item.Enabled = (Tree.SelectedNode != null);
            }
            if (watchMode) watchMenuItem.Enabled = (Tree.SelectedNode != null && Tree.SelectedNode.Level == 1);
        }

        void TreeExpanding(Object sender, TreeViewAdvEventArgs e)
        {
            if (e.Node.Index >= 0)
            {
                ValueNode node = e.Node.Tag as ValueNode;
                if (node != null && node.Nodes.Count == 0 && node.PlayerValue != null)
                {
                    FlashInterface flashInterface = PluginMain.debugManager.FlashInterface;
                    List<VariableNode> nodes = new List<VariableNode>();
                    List<VariableNode> inherited = new List<VariableNode>();
                    List<VariableNode> statics = new List<VariableNode>();
                    int tmpLimit = node.ChildrenShowLimit;
                    foreach (Variable member in node.PlayerValue.getMembers(flashInterface.Session))
                    {
                        VariableNode memberNode = new VariableNode(member);
                        
                        if (member.isAttributeSet(VariableAttribute_.IS_STATIC))
                        {
                            statics.Add(memberNode);
                        }
                        else if (member.getLevel() > 0)
                        {
                            inherited.Add(memberNode);
                        }
                        else
                        {
                            nodes.Add(memberNode);
                        }
                    }
                    if (inherited.Count > 0)
                    {
                        ValueNode inheritedNode = new ValueNode("[inherited]");
                        inherited.Sort();
                        foreach (DataNode item in inherited)
                        {
                            inheritedNode.Nodes.Add(item);
                        }
                        node.Nodes.Add(inheritedNode);
                    }
                    if (statics.Count > 0)
                    {
                        DataNode staticNode = new ValueNode("[static]");
                        statics.Sort();
                        foreach (DataNode item in statics)
                        {
                            staticNode.Nodes.Add(item);
                        }
                        node.Nodes.Add(staticNode);
                    }
                    //test children
                    foreach (String ch in node.PlayerValue.getClassHierarchy(false))
                    {
                        if (ch.Equals("flash.display::DisplayObjectContainer"))
                        {
                            double numChildren = ((java.lang.Double)node.PlayerValue.getMemberNamed(flashInterface.Session, "numChildren").getValue().getValueAsObject()).doubleValue();
                            DataNode childrenNode = new ValueNode("[children]");
                            for (int i = 0; i < numChildren; i++)
                            {
                                try
                                {
                                    IASTBuilder b = new ASTBuilder(false);
                                    string cmd = node.GetVariablePath() + ".getChildAt(" + i + ")";
                                    ValueExp exp = b.parse(new java.io.StringReader(cmd));
                                    var ctx = new ExpressionContext(flashInterface.Session, flashInterface.GetFrames()[PluginMain.debugManager.CurrentFrame]);
                                    var obj = exp.evaluate(ctx);
                                    if (obj is flash.tools.debugger.concrete.DValue) obj = new flash.tools.debugger.concrete.DVariable("getChildAt(" + i + ")", (flash.tools.debugger.concrete.DValue)obj, ((flash.tools.debugger.concrete.DValue)obj).getIsolateId());
                                    DataNode childNode = new VariableNode((Variable)obj);
                                    childNode.Text = "child_" + i;
                                    childrenNode.Nodes.Add(childNode);
                                }
                                catch (Exception) { }
                            }
                            node.Nodes.Add(childrenNode);
                        }
                        else if (ch.Equals("flash.events::EventDispatcher"))
                        {
                            Variable list = node.PlayerValue.getMemberNamed(flashInterface.Session, "listeners");
                            var omg = list.getName();
                            /*
                            double numChildren = ((java.lang.Double)node.Variable.getValue().getMemberNamed(flashInterface.Session, "numChildren").getValue().getValueAsObject()).doubleValue();
                            DataNode childrenNode = new DataNode("[children]");
                            for (int i = 0; i < numChildren; i++)
                            {
                                try
                                {

                                    IASTBuilder b = new ASTBuilder(false);
                                    string cmd = GetVariablePath(node) + ".getChildAt(" + i + ")";
                                    ValueExp exp = b.parse(new java.io.StringReader(cmd));
                                    var ctx = new ExpressionContext(flashInterface.Session, flashInterface.Session.getFrames()[PluginMain.debugManager.CurrentFrame]);
                                    var obj = exp.evaluate(ctx);
                                    if (obj is flash.tools.debugger.concrete.DValue) obj = new flash.tools.debugger.concrete.DVariable("child_" + i, (flash.tools.debugger.concrete.DValue)obj);
                                    DataNode childNode = new DataNode((Variable)obj);
                                    childrenNode.Nodes.Add(childNode);
                                }
                                catch (Exception) { }
                            }
                            node.Nodes.Add(childrenNode);
                             * */
                        }
                    }
                    //test children
                    nodes.Sort();
                    _tree.BeginUpdate();
                    foreach (DataNode item in nodes)
                    {
                        if (0 == tmpLimit--) break;
                        node.Nodes.Add(item);
                    }
                    if (tmpLimit == -1)
                    {
                        DataNode moreNode = new ValueNode("...");
                        node.Nodes.Add(moreNode);
                    }
                    _tree.EndUpdate();
                }
            }
        }

        void Tree_NodeMouseDoubleClick(object sender, TreeNodeAdvMouseEventArgs e)
        {
            ValueNode node = e.Node.Tag as ValueNode;
            if (node != null && node.Text == "..." && node.PlayerValue == null)
            {
                e.Handled = true;
                _tree.BeginUpdate();
                (node.Parent as ValueNode).ChildrenShowLimit += 500;
                TreeNodeAdv parent = e.Node.Parent;
                int ind = e.Node.Index;
                parent.Collapse(true);
                node.Parent.Nodes.Clear();
                parent.Expand(true);
                _tree.EndUpdate();
                if (parent.Children.Count>ind) _tree.ScrollTo(parent.Children[ind]);
            }
        }

        public void SaveExpanded()
        {
            expandedList.Clear();
            SaveExpanded(Nodes);
        }

        private void SaveExpanded(Collection<Node> nodes)
        {
            if (nodes == null) return;
            foreach (Node node in nodes)
            {
                if (Tree.FindNode(_model.GetPath(node)).IsExpanded)
                {
                    expandedList.Add(_model.GetFullPath(node));
                    SaveExpanded(node.Nodes);
                }
            }
        }

        public void RestoreExpanded()
        {
            RestoreExpanded(Nodes);
        }

        private void RestoreExpanded(Collection<Node> nodes)
        {
            if (nodes == null) return;
            foreach (Node node in nodes)
            {
                if (expandedList.Contains(_model.GetFullPath(node)))
                {
                    Tree.FindNode(_model.GetPath(node)).Expand();
                    RestoreExpanded(node.Nodes);
                }
            }
        }


        #region IToolTipProvider Members

        public string GetToolTip(TreeNodeAdv node, NodeControl nodeControl)
        {
            DataNode dataNode = node.Tag as DataNode;
            string r;
            if (dataNode is ValueNode)
            {
                var vNode = (ValueNode)dataNode;
                // use IsEditing to get unfiltered value
                bool e = vNode.IsEditing;
                vNode.IsEditing = true;
                r = dataNode.Value;
                vNode.IsEditing = e;
            }
            else
                r = dataNode.Value;
            if (r.Length > 300) r = r.Substring(0, 300) + "[...]";
            return r;
        }

        #endregion
    }

}

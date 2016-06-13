using System;
using System.Text;
using VRage.Input;
using VRageMath;
using System.Collections.Generic;
using System.Linq;

namespace Sandbox.Graphics.GUI
{
    public class MyTreeView
    {
        private MyGuiControlTreeView m_control;

        private MyTreeViewBody m_body;
        private MyHScrollbar m_hScrollbar;
        private MyVScrollbar m_vScrollbar;
        private Vector2 m_scrollbarSize;

        public MyTreeViewBase Body
        {
            get { return m_body; }
        }

        private List<MyTreeViewItem> selection = new List<MyTreeViewItem>();
        public IEnumerable<MyTreeViewItem> Selection
        {
            get { return selection; }
            set
            {
                if ( Enumerable.SequenceEqual( selection, value ) )
                    return;

                SetSelection( value );
            }
        }

        public void SetSelection( IEnumerable<MyTreeViewItem> newSel )
        {
            var oldSelection = selection.ToArray();

            foreach ( var item in selection )
            {
                item.Selected = false;
            }

            selection.Clear();
            selection.AddRange( newSel );

            foreach ( var item in selection )
            {
                item.Selected = true;
            }

            var firstSel = selection.Count == 0 ? null : selection[ selection.Count - 1 ];
            if ( firstSel != null )
            {
                Vector2 offset = MyGUIHelper.GetOffset( Position, m_body.GetSize(), firstSel.GetPosition(), firstSel.GetSize() );

                m_vScrollbar.ChangeValue( -offset.Y );
                m_hScrollbar.ChangeValue( -offset.X );
            }


            if ( OnSelectionChanged != null )
            {
                OnSelectionChanged( this, oldSelection );
            }
        }

        public Action<MyTreeView, IEnumerable<MyTreeViewItem>> OnSelectionChanged;

       
        private MyTreeViewItem hooveredItem;
        public MyTreeViewItem HooveredItem
        {
            get { return hooveredItem; }
            internal set { hooveredItem = value; }
        }

        public MyTreeView(MyGuiControlTreeView control)
        {
            m_control = control;

            m_body = new MyTreeViewBody(this, Vector2.Zero );
            m_vScrollbar = new MyVScrollbar(control);
            m_hScrollbar = new MyHScrollbar(control);
            m_scrollbarSize = new Vector2(MyGuiConstants.TREEVIEW_VSCROLLBAR_SIZE.X, MyGuiConstants.TREEVIEW_HSCROLLBAR_SIZE.Y);
        }

        public Vector2 Position
        {
            get { return m_control.GetPositionAbsoluteTopLeft(); }
        }

        public Vector2 Size
        {
            get { return m_control.Size; }
        }

        public Vector2 GetBodySize()
        {
            return m_body.GetSize();
        }

        public void Layout()
        {
            m_body.Layout(Position, Vector2.Zero );

            Vector2 realSize = m_body.GetRealSize();

            bool scrollbarsVisible = Size.Y - m_scrollbarSize.Y < realSize.Y && Size.X - m_scrollbarSize.X < realSize.X;
            bool vScrollbarVisible = scrollbarsVisible || Size.Y < realSize.Y;
            bool hScrollbarVisible = scrollbarsVisible || Size.X < realSize.X;

            Vector2 bodySize = new Vector2( vScrollbarVisible ? Size.X - m_scrollbarSize.X : Size.X, hScrollbarVisible ? Size.Y - m_scrollbarSize.Y : Size.Y );

            m_vScrollbar.Visible = vScrollbarVisible;
            m_vScrollbar.Init(realSize.Y, bodySize.Y);

            //m_vScrollbar.Layout(m_body.GetPosition() + new Vector2(m_scrollbarSize.X / 4f - 0.0024f, 0), m_body.GetSize(), new Vector2(m_scrollbarSize.X / 2f, m_scrollbarSize.Y), hScrollbarVisible);
            m_vScrollbar.Layout( Position + new Vector2( m_scrollbarSize.X / 4f - 0.0024f + Size.X / 2 - 0.070f, 0 - Size.Y + .070f ), Size.Y );
            m_vScrollbar.Visible = vScrollbarVisible;

            m_hScrollbar.Visible = hScrollbarVisible;
            m_hScrollbar.Init(realSize.X, bodySize.X);
            //m_hScrollbar.Layout(m_body.GetPosition(), m_body.GetSize(), m_scrollbarSize, vScrollbarVisible);
            m_hScrollbar.Layout( Position + new Vector2( -0.075f, +0.025f ), Size.X / 4.0f );
            m_hScrollbar.Visible = hScrollbarVisible;

            m_body.SetSize(bodySize);
            m_body.Layout(Position, new Vector2(m_hScrollbar.Value, m_vScrollbar.Value));
        }

        private void TraverseVisible(ITreeView iTreeView, Action<MyTreeViewItem> action)
        {
            for (int i = 0; i < iTreeView.GetItemCount(); i++)
            {
                var item = iTreeView.GetItem(i);

                if (item.Visible)
                {
                    action(item);
                    if (item.IsExpanded)
                    {
                        TraverseVisible(item, action);
                    }
                }
            }
        }

        private MyTreeViewItem NextVisible(ITreeView iTreeView, MyTreeViewItem focused)
        {
            bool found = false;
            TraverseVisible(m_body, a =>
            {
                if (a == focused)
                {
                    found = true;
                }
                else if (found)
                {
                    focused = a;
                    found = false;
                }
            }
            );
            return focused;
        }

        private MyTreeViewItem PrevVisible(ITreeView iTreeView, MyTreeViewItem focused)
        {
            MyTreeViewItem pred = focused;
            TraverseVisible(m_body, a =>
            {
                if (a == focused)
                {
                    focused = pred;
                }
                else
                {
                    pred = a;
                }
            }
            );
            return focused;
        }

        public bool HandleInput()
        {
            var oldHooveredItem = hooveredItem;
            hooveredItem = null;

            bool captured = m_body.HandleInput(m_control.HasFocus) ||
                            m_vScrollbar.HandleInput() ||
                            m_hScrollbar.HandleInput();

            var lastSelectedItem = selection.Count != 0 ? selection[ selection.Count - 1 ] : null;

            if ( m_control.HasFocus )
            {
                if ( lastSelectedItem == null &&
                    m_body.GetItemCount() > 0 &&
                    (MyInput.Static.IsNewKeyPressed(MyKeys.Up) ||
                     MyInput.Static.IsNewKeyPressed(MyKeys.Down) ||
                     MyInput.Static.IsNewKeyPressed(MyKeys.Left) ||
                     MyInput.Static.IsNewKeyPressed(MyKeys.Right) ||
                     MyInput.Static.DeltaMouseScrollWheelValue() != 0))
                {
                    Selection = new MyTreeViewItem[] { m_body[0] };
                }
                else if ( lastSelectedItem != null )
                {
                    /*
                    if (MyInput.Static.IsNewKeyPressed(MyKeys.Down) || (MyInput.Static.DeltaMouseScrollWheelValue() < 0 && Contains(MyGuiManager.MouseCursorPosition.X, MyGuiManager.MouseCursorPosition.Y)))
                    {
                        var sel = NextVisible( m_body, lastSelectedItem );
                        Selection = sel != null ? new MyTreeViewItem[] { sel } : new MyTreeViewItem[]{};
                    }

                    if (MyInput.Static.IsNewKeyPressed(MyKeys.Up) || (MyInput.Static.DeltaMouseScrollWheelValue() > 0 && Contains(MyGuiManager.MouseCursorPosition.X, MyGuiManager.MouseCursorPosition.Y)))
                    {
                        var sel = PrevVisible( m_body, lastSelectedItem );
                        Selection = sel != null ? new MyTreeViewItem[] { sel } : new MyTreeViewItem[] { };
                    }
                     * */

                    if (MyInput.Static.IsNewKeyPressed(MyKeys.Right))
                    {
                        if ( lastSelectedItem.GetItemCount() > 0 )
                        {
                            if ( !lastSelectedItem.IsExpanded )
                            {
                                lastSelectedItem.IsExpanded = true;
                            }
                            else
                            {
                                var sel = NextVisible( lastSelectedItem, lastSelectedItem );
                                Selection = sel != null ? new MyTreeViewItem[] { sel } : new MyTreeViewItem[] { };
                            }
                        }
                    }

                    if (MyInput.Static.IsNewKeyPressed(MyKeys.Left))
                    {
                        if ( lastSelectedItem.GetItemCount() > 0 && lastSelectedItem.IsExpanded )
                        {
                            lastSelectedItem.IsExpanded = false;
                        }
                        else if ( lastSelectedItem.Parent is MyTreeViewItem )
                        {
                            var sel = lastSelectedItem.Parent as MyTreeViewItem;
                            Selection = sel != null ? new MyTreeViewItem[] { sel } : new MyTreeViewItem[] { };
                        }
                    }

                    if ( lastSelectedItem.GetItemCount() > 0 )
                    {
                        if (MyInput.Static.IsNewKeyPressed(MyKeys.Add))
                        {
                            lastSelectedItem.IsExpanded = true;
                        }

                        if (MyInput.Static.IsNewKeyPressed(MyKeys.Subtract))
                        {
                            lastSelectedItem.IsExpanded = false;
                        }
                    }
                }

                if (MyInput.Static.IsNewKeyPressed(MyKeys.PageDown))
                {
                    m_vScrollbar.PageDown();
                }

                if (MyInput.Static.IsNewKeyPressed(MyKeys.PageUp))
                {
                    m_vScrollbar.PageUp();
                }

                captured = captured ||
                           MyInput.Static.IsNewKeyPressed(MyKeys.PageDown) ||
                           MyInput.Static.IsNewKeyPressed(MyKeys.PageUp) ||
                           MyInput.Static.IsNewKeyPressed(MyKeys.Down) ||
                           MyInput.Static.IsNewKeyPressed(MyKeys.Up) ||
                           MyInput.Static.IsNewKeyPressed(MyKeys.Left) ||
                           MyInput.Static.IsNewKeyPressed(MyKeys.Right) ||
                           MyInput.Static.IsNewKeyPressed(MyKeys.Add) ||
                           MyInput.Static.IsNewKeyPressed(MyKeys.Subtract) ||
                           MyInput.Static.DeltaMouseScrollWheelValue() != 0;
            }

            // Hoovered item changed
            if (hooveredItem != oldHooveredItem)
            {
                m_control.ShowToolTip(hooveredItem == null ? null : hooveredItem.ToolTip);
                MyGuiSoundManager.PlaySound(GuiSounds.MouseOver);
            }

            return captured;
        }

        public MyTreeViewItem AddItem(StringBuilder text, string icon, Vector2 iconSize, string expandIcon, string collapseIcon, Vector2 expandIconSize)
        {
            return m_body.AddItem(text, icon, iconSize, expandIcon, collapseIcon, expandIconSize);
        }

        public void DeleteItem(MyTreeViewItem item)
        {
            int selIndex = selection.IndexOf( item );
            if ( selIndex != -1 )
            {
                selection.RemoveAt( selIndex );
                /*
                int index = item.GetIndex();
                if ( index + 1 < GetItemCount() )
                {
                    FocusedItem = GetItem( index + 1 );
                }
                else if ( index - 1 >= 0 )
                {
                    FocusedItem = GetItem( index - 1 );
                }
                else
                {
                    FocusedItem = FocusedItem.Parent as MyTreeViewItem;
                }
                 * */
            }

            m_body.DeleteItem(item);
        }

        public void ClearItems()
        {
            m_body.ClearItems();
        }

        public void Draw(float transitionAlpha)
        {
            var scissor = new RectangleF(Position, m_body.GetSize());
            using (MyGuiManager.UsingScissorRectangle(ref scissor))
            {
                m_body.Draw(transitionAlpha);
            }

            Color borderColor = MyGuiControlBase.ApplyColorMaskModifiers(MyGuiConstants.TREEVIEW_VERTICAL_LINE_COLOR, true, transitionAlpha);
            MyGUIHelper.OutsideBorder(Position, Size, 2, borderColor);

            m_vScrollbar.Draw(Color.White);
            m_hScrollbar.Draw(Color.White);
        }

        public bool Contains(Vector2 position, Vector2 size)
        {
            return MyGUIHelper.Intersects( Position, m_body.GetSize(), position, size );
        }

        public bool Contains(float x, float y)
        {
            return MyGUIHelper.Contains(Position, m_body.GetSize(), x, y);
        }

        public Color GetColor(Vector4 color, float transitionAlpha)
        {
            return MyGuiControlBase.ApplyColorMaskModifiers(color, true, transitionAlpha);
        }

        public bool WholeRowHighlight()
        {
            return m_control.WholeRowHighlight;
        }

        public IEnumerable<MyTreeViewItem> Items
        {
            get { return m_body.Items;}
        }

        public MyTreeViewItem GetItem(int index)
        {
            return m_body[index];
        }

        public MyTreeViewItem GetItem(StringBuilder name)
        {
            return m_body.GetItem(name);
        }

        public int GetItemCount()
        {
            return m_body.GetItemCount();
        }

        public static bool FilterTree(ITreeView treeView, Predicate<MyTreeViewItem> itemFilter)
        {
            int visibleCount = 0;
            for (int i = 0; i < treeView.GetItemCount(); i++)
            {
                var item = treeView.GetItem(i);

                if (FilterTree(item, itemFilter) || (item.GetItemCount() == 0 && itemFilter(item)))
                {
                    item.Visible = true;
                    ++visibleCount;
                }
                else
                {
                    item.Visible = false;
                }
            }
            return visibleCount > 0;
        }
    }
}

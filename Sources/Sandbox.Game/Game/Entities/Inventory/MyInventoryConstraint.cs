using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Sandbox.Graphics.GUI;
using Sandbox.Definitions;
using Sandbox.Common.ObjectBuilders;

using Sandbox.Engine.Utils;
using VRage;
using VRage.Game;
using VRage.Utils;
using VRage.Library.Utils;
using VRage.ObjectBuilders;

namespace Sandbox.Game
{
    public class MyInventoryConstraint
    {
        public string Icon;
        public bool m_useDefaultIcon = false;
        private String descriptionText;
        private MyStringId descriptionID;
        private String generatedDescription;

        private bool m_shouldRebuildDescription = true;

        public String Description
        {
            get
            {
                if ( m_shouldRebuildDescription )
                {
                    UpdateDescription();
                }

                return generatedDescription;
            }
        }

        private HashSet<MyDefinitionId> m_constrainedIds;
        private HashSet<MyObjectBuilderType> m_constrainedTypes;

        public bool IsWhitelist
        {
            get;
            set;
        }

        public IEnumerable<MyDefinitionId> ConstrainedIds
        {
            get { return m_constrainedIds.Skip(0); }
        }
        public IEnumerable<MyObjectBuilderType> ConstrainedTypes
        {
            get { return m_constrainedTypes.Skip(0); }
        }

        public MyInventoryConstraint(MyStringId description, string icon = null, bool whitelist = true)
        {
            Icon = icon;
            m_useDefaultIcon = icon == null;
            descriptionID = description;

            m_constrainedIds = new HashSet<MyDefinitionId>();
            m_constrainedTypes = new HashSet<MyObjectBuilderType>();
            IsWhitelist = whitelist;
        }

        private void UpdateDescription()
        {
            if ( m_constrainedIds.Count + m_constrainedTypes.Count > 0 )
            {
                generatedDescription = string.IsNullOrEmpty( descriptionText ) ? MyTexts.GetString( descriptionID ) : descriptionText;

                if ( m_constrainedTypes.Count > 0 )
                {
                    generatedDescription += "\r\n\r\nItem Types:\r\n";
                    foreach ( var type in m_constrainedTypes )
                    {
                        // fixme: get from localization
                        generatedDescription += String.Format( "\t{0}\r\n", type );
                    }
                }

                if ( m_constrainedIds.Count > 0 )
                {
                    var itemNames = new List<string>();
                    foreach ( var id in m_constrainedIds )
                    {
                        var item = MyDefinitionManager.Static.TryGetPhysicalItemDefinition( id );
                        itemNames.Add( item != null ? item.DisplayNameText : id.ToString() );
                    }

                    itemNames.Sort();

                    bool isMultyElementsPerLine = m_constrainedIds.Count > 64;
                    generatedDescription += "\r\n\r\nItems:\r\n";
                    int nAddedElements = 0;
                    bool isNewLine = true;
                    foreach ( var itemName in itemNames )
                    {
                        if ( isNewLine )
                        {
                            generatedDescription += "\t";
                            isNewLine = false;
                        }

                        generatedDescription += itemName;

                        bool shouldBreakLine = isMultyElementsPerLine ? ( nAddedElements % 3 == 0 ) : true;
                        ++nAddedElements;
                        if ( shouldBreakLine )
                        {
                            generatedDescription += "\r\n";
                            isNewLine = true;
                        }
                        else
                        {
                            generatedDescription += ", ";
                        }
                    }
                }

                generatedDescription.Remove( generatedDescription.Length - 2, 2 );
            } else
            {
                generatedDescription = string.IsNullOrEmpty( descriptionText ) ? MyTexts.GetString( descriptionID ) : descriptionText;

                generatedDescription += "\r\n\r\nNo acceptible items or classes";

            }

            m_shouldRebuildDescription = false;

        }

        public MyInventoryConstraint(String description, string icon = null, bool whitelist = true)
        {
            Icon = icon;
            m_useDefaultIcon = icon == null;

            descriptionText = description;
            m_constrainedIds = new HashSet<MyDefinitionId>();
            m_constrainedTypes = new HashSet<MyObjectBuilderType>();
            IsWhitelist = whitelist;
        }

        public MyInventoryConstraint Add(MyDefinitionId id)
        {
            m_constrainedIds.Add(id);
            m_shouldRebuildDescription = true;
            UpdateIcon();
            return this;
        }

        public MyInventoryConstraint Remove(MyDefinitionId id)
        {

            m_constrainedIds.Remove(id);
            m_shouldRebuildDescription = true;
            UpdateIcon();
            return this;
        }

        public MyInventoryConstraint AddObjectBuilderType(MyObjectBuilderType type)
        {
            m_constrainedTypes.Add(type);
            m_shouldRebuildDescription = true;
            UpdateIcon();
            return this;
        }

        public MyInventoryConstraint RemoveObjectBuilderType(MyObjectBuilderType type)
        {
            m_constrainedTypes.Remove(type);
            m_shouldRebuildDescription = true;
            UpdateIcon();
            return this;
        }

        public bool Check(MyDefinitionId checkedId)
        {
            if (IsWhitelist)
            {
                if (m_constrainedTypes.Contains(checkedId.TypeId))
                    return true;

                if (m_constrainedIds.Contains(checkedId))
                    return true;
                return false;
            }
            else
            {
                if (m_constrainedTypes.Contains(checkedId.TypeId))
                    return false;

                if (m_constrainedIds.Contains(checkedId))
                    return false;
                return true;
            }
        }

        // Updates icon according to the filtered items
        // CH: TODO: This is temporary. It should be somewhere in the definitions:
        //     either in the block definitions or have an extra definition file for inventory constraints
        public void UpdateIcon()
        {
            if (!m_useDefaultIcon) return;

            if (m_constrainedIds.Count == 0 && m_constrainedTypes.Count == 1)
            {
                var type = m_constrainedTypes.First();
                if (type == typeof(MyObjectBuilder_Ore))
                    Icon = MyGuiConstants.TEXTURE_ICON_FILTER_ORE;
                else if (type == typeof(MyObjectBuilder_Ingot))
                    Icon = MyGuiConstants.TEXTURE_ICON_FILTER_INGOT;
                else if (type == typeof(MyObjectBuilder_Component))
                    Icon = MyGuiConstants.TEXTURE_ICON_FILTER_COMPONENT;
            }
            else if (m_constrainedIds.Count == 1 && m_constrainedTypes.Count == 0)
            {
                var id = m_constrainedIds.First();
                if (id == new MyDefinitionId(typeof(MyObjectBuilder_Ingot), "Uranium"))
                    Icon = MyGuiConstants.TEXTURE_ICON_FILTER_URANIUM;
                // MW: Right now weapon can have multiple types of ammo magazines
                //else if (id == new MyDefinitionId(typeof(MyObjectBuilder_AmmoMagazine), "Missile200mm"))
                //    Icon = MyGuiConstants.TEXTURE_ICON_FILTER_MISSILE;
                //else if (id == new MyDefinitionId(typeof(MyObjectBuilder_AmmoMagazine), "NATO_5p56x45mm"))
                //    Icon = MyGuiConstants.TEXTURE_ICON_FILTER_AMMO_5_54MM;
                //else if (id == new MyDefinitionId(typeof(MyObjectBuilder_AmmoMagazine), "NATO_25x184mm"))
                //    Icon = MyGuiConstants.TEXTURE_ICON_FILTER_AMMO_25MM;
            }
            else Icon = null;
        }
    }
}

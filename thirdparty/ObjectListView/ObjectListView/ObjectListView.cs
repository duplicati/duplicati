/*
 * ObjectListView - A listview to show various aspects of a collection of objects
 *
 * Author: Phillip Piper
 * Date: 9/10/2006 11:15 AM
 *
 * Change log
 * 2011-05-31  JPP  - SelectObject() and SelectObjects() no longer deselect all other rows.
                      Set the SelectedObject or SelectedObjects property to do that.
 *                  - Added CheckedObjectsEnumerable
 *                  - Made setting CheckedObjects more efficient on large collections
 *                  - Deprecated GetSelectedObject() and GetSelectedObjects()
 * 2011-04-25  JPP  - Added SubItemChecking event
 *                  - Fixed bug in handling of NewValue on CellEditFinishing event
 * 2011-04-12  JPP  - Added UseFilterIndicator 
 *                  - Added some more localizable messages
 * 2011-04-10  JPP  - FormatCellEventArgs now has a CellValue property, which is the model value displayed
 *                    by the cell. For example, for the Birthday column, the CellValue might be 
 *                    DateTime(1980, 12, 31), whereas the cell's text might be 'Dec 31, 1980'.
 * 2011-04-04  JPP  - Tweaked UseTranslucentSelection and UseTranslucentHotItem to look (a little) more
 *                    like Vista/Win7.
 *                  - Alternate colours are now only applied in Details view (as they always should have been)
 *                  - Alternate colours are now correctly recalculated after removing objects
 * 2011-03-29  JPP  - Added SelectColumnsOnRightClickBehaviour to allow the selecting of columns mechanism 
 *                    to be changed. Can now be InlineMenu (the default), SubMenu, or ModelDialog.
 *                  - ColumnSelectionForm was moved from the demo into the ObjectListView project itself.
 *                  - Ctrl-C copying is now able to use the DragSource to create the data transfer object.
 * 2011-03-19  JPP  - All model object comparisons now use Equals rather than == (thanks to vulkanino)
 *                  - [Small Break] GetNextItem() and GetPreviousItem() now accept and return OLVListView
 *                    rather than ListViewItems.
 * 2011-03-07  JPP  - [Big] Added Excel-style filtering. Right click on a header to show a Filtering menu.
 *                  - Added CellEditKeyEngine to allow key handling when cell editing to be completely customised.
 *                    Add CellEditTabChangesRows and CellEditEnterChangesRows to show some of these abilities.
 * 2011-03-06  JPP  - Added OLVColumn.AutoCompleteEditorMode in preference to AutoCompleteEditor 
 *                    (which is now just a wrapper). Thanks to Clive Haskins 
 *                  - Added lots of docs to new classes
 * 2011-02-25  JPP  - Preserve word wrap settings on TreeListView
 *                  - Resize last group to keep it on screen (thanks to ?)
 * 2010-11-16  JPP  - Fixed (once and for all) DisplayIndex problem with Generator
 *                  - Changed the serializer used in SaveState()/RestoreState() so that it resolves on
 *                    class name alone.
 *                  - Fixed bug in GroupWithItemCountSingularFormatOrDefault
 *                  - Fixed strange flickering in grouped, owner drawn OLV's using RefreshObject()
 * v2.4.1
 * 2010-08-25  JPP  - Fixed bug where setting OLVColumn.CheckBoxes to false gave it a renderer
 *                    specialized for checkboxes. Oddly, this made Generator created owner drawn
 *                    lists appear to be completely empty.
 *                  - In IDE, all ObjectListView properties are now in a single "ObjectListView" category,
 *                    rather than splitting them between "Appearance" and "Behavior" categories.
 *                  - Added GroupingParameters.GroupComparer to allow groups to be sorted in a customizable fashion.
 *                  - Sorting of items within a group can be disabled by setting 
 *                    GroupingParameters.PrimarySortOrder to None.
 * 2010-08-24  JPP  - Added OLVColumn.IsHeaderVertical to make a column draw its header vertical.
 *                  - Added OLVColumn.HeaderTextAlign to control the alignment of a column's header text.
 *                  - Added HeaderMaximumHeight to limit how tall the header section can become
 * 2010-08-18  JPP  - Fixed long standing bug where having 0 columns caused a InvalidCast exception.
 *                  - Added IncludeAllColumnsInDataObject property
 *                  - Improved BuildList(bool) so that it preserves scroll position even when
 *                    the listview is grouped.
 * 2010-08-08  JPP  - Added OLVColumn.HeaderImageKey to allow column headers to have an image.
 *                  - CellEdit validation and finish events now have NewValue property.
 * 2010-08-03  JPP  - Subitem checkboxes improvments: obey IsEditable, can be hot, can be disabled.
 *                  - No more flickering of selection when tabbing between cells
 *                  - Added EditingCellBorderDecoration to make it clearer which cell is being edited.
 * 2010-08-01  JPP  - Added ObjectListView.SmoothingMode to control the smoothing of all graphics
 *                    operations
 *                  - Columns now cache their group item format strings so that they still work as 
 *                    grouping columns after they have been removed from the listview. This cached
 *                    value is only used when the column is not part of the listview.
 * 2010-07-25  JPP  - Correctly trigger a Click event when the mouse is clicked.
 * 2010-07-16  JPP  - Invalidate the control before and after cell editing to make sure it looks right
 * 2010-06-23  JPP  - Right mouse clicks on checkboxes no longer confuse them
 * 2010-06-21  JPP  - Avoid bug in underlying ListView control where virtual lists in SmallIcon view
 *                    generate GETTOOLINFO msgs with invalid item indicies.
 *                  - Fixed bug where FastObjectListView would throw an exception when showing hyperlinks
 *                    in any view except Details.
 * 2010-06-15  JPP  - Fixed bug in ChangeToFilteredColumns() that resulted in column display order
 *                    being lost when a column was hidden.
 *                  - Renamed IsVista property to IsVistaOrLater which more accurately describes its function.
 * v2.4
 * 2010-04-14  JPP  - Prevent object disposed errors when mouse event handlers cause the
 *                    ObjectListView to be destroyed (e.g. closing a form during a 
 *                    double click event).
 *                  - Avoid checkbox munging bug in standard ListView when shift clicking on non-primary
 *                    columns when FullRowSelect is true.
 * 2010-04-12  JPP  - Fixed bug in group sorting (thanks Mike).
 * 2010-04-07  JPP  - Prevent hyperlink processing from triggering spurious MouseUp events.
 *                    This showed itself by launching the same url multiple times.
 * 2010-04-06  JPP  - Space filling columns correctly resize upon initial display
 *                  - ShowHeaderInAllViews is better but still not working reliably.
 *                    See comments on property for more details.
 * 2010-03-23  JPP  - Added ObjectListView.HeaderFormatStyle and OLVColumn.HeaderFormatStyle.
 *                    This makes HeaderFont and HeaderForeColor properties unnecessary -- 
 *                    they will be marked obsolete in the next version and removed after that.
 * 2010-03-16  JPP  - Changed object checking so that objects can be pre-checked before they
 *                    are added to the list. Normal ObjectListViews managed "checkedness" in
 *                    the ListViewItem, so this won't work for them, unless check state getters
 *                    and putters have been installed. It will work on on virtual lists (thus fast lists and
 *                    tree views) since they manage their own check state.
 * 2010-03-06  JPP  - Hide "Items" and "Groups" from the IDE properties grid since they shouldn't be set like that.
 *                    They can still be accessed through "Custom Commands" and there's nothing we can do
 *                    about that.
 * 2010-03-05  JPP  - Added filtering
 * 2010-01-18  JPP  - Overlays can be turned off. They also only work on 32-bit displays
 * v2.3
 * 2009-10-30  JPP  - Plugged possible resource leak by using using() with CreateGraphics()
 * 2009-10-28  JPP  - Fix bug when right clicking in the empty area of the header
 * 2009-10-20  JPP  - Redraw the control after setting EmptyListMsg property
 * v2.3
 * 2009-09-30  JPP  - Added Dispose() method to properly release resources
 * 2009-09-16  JPP  - Added OwnerDrawnHeader, which you can set to true if you want to owner draw
 *                    the header yourself.
 * 2009-09-15  JPP  - Added UseExplorerTheme, which allow complete visual compliance with Vista explorer.
 *                    But see property documentation for its many limitations.
 *                  - Added ShowHeaderInAllViews. To make this work, Columns are no longer
 *                    changed when switching to/from Tile view.
 * 2009-09-11  JPP  - Added OLVColumn.AutoCompleteEditor to allow the autocomplete of cell editors
 *                    to be disabled.
 * 2009-09-01  JPP  - Added ObjectListView.TextRenderingHint property which controls the
 *                    text rendering hint of all drawn text.
 * 2009-08-28  JPP  - [BIG] Added group formatting to supercharge what is possible with groups
 *                  - [BIG] Virtual groups now work
 *                  - Extended MakeGroupies() to handle more aspects of group creation
 * 2009-08-19  JPP  - Added ability to show basic column commands when header is right clicked
 *                  - Added SelectedRowDecoration, UseTranslucentSelection and UseTranslucentHotItem.
 *                  - Added PrimarySortColumn and PrimarySortOrder
 * 2009-08-15  JPP  - Correct problems with standard hit test and subitems
 * 2009-08-14  JPP  - [BIG] Support Decorations
 *                  - [BIG] Added header formatting capabilities: font, color, word wrap
 *                  - Gave ObjectListView its own designer to hide unwanted properties
 *                  - Separated design time stuff into separate file
 *                  - Added FormatRow and FormatCell events
 * 2009-08-09  JPP  - Get around bug in HitTest when not FullRowSelect
 *                  - Added OLVListItem.GetSubItemBounds() method which works correctly
 *                    for all columns including column 0
 * 2009-08-07  JPP  - Added Hot* properties that track where the mouse is
 *                  - Added HotItemChanged event
 *                  - Overrode TextAlign on columns so that column 0 can have something other
 *                    than just left alignment. This is only honored when owner drawn.
 * v2.2.1
 * 2009-08-03  JPP  - Subitem edit rectangles always allowed for an image in the cell, even if there was none.
 *                    Now they only allow for an image when there actually is one.
 *                  - Added Bounds property to OLVListItem which handles items being part of collapsed groups.
 * 2009-07-29  JPP  - Added GetSubItem() methods to ObjectListView and OLVListItem
 * 2009-07-26  JPP  - Avoided bug in .NET framework involving column 0 of owner drawn listviews not being
 *                    redrawn when the listview was scrolled horizontally (this was a LOT of work to track
 *                    down and fix!)
 *                  - The cell edit rectangle is now correctly calculated when the listview is scrolled
 *                    horizontally.
 * 2009-07-14  JPP  - If the user clicks/double clicks on a tree list cell, an edit operation will no longer begin
 *                    if the click was to the left of the expander. This is implemented in such a way that
 *                    other renderers can have similar "dead" zones.
 * 2009-07-11  JPP  - CalculateCellBounds() messed with the FullRowSelect property, which confused the
 *                    tooltip handling on the underlying control. It no longer does this.
 *                  - The cell edit rectangle is now correctly calculated for owner-drawn, non-Details views.
 * 2009-07-08  JPP  - Added Cell events (CellClicked, CellOver, CellRightClicked)
 *                  - Made BuildList(), AddObject() and RemoveObject() thread-safe
 * 2009-07-04  JPP  - Space bar now properly toggles checkedness of selected rows
 * 2009-07-02  JPP  - Fixed bug with tooltips when the underlying Windows control was destroyed.
 *                  - CellToolTipShowing events are now triggered in all views.
 * v2.2
 * 2009-06-02  JPP  - BeforeSortingEventArgs now has a Handled property to let event handlers do
 *                    the item sorting themselves.
 *                  - AlwaysGroupByColumn works again, as does SortGroupItemsByPrimaryColumn and all their
 *                    various permutations.
 *                  - SecondarySortOrder and SecondarySortColumn are now "null" by default
 * 2009-05-15  JPP  - Fixed bug so that KeyPress events are again triggered
 * 2009-05-10  JPP  - Removed all unsafe code
 * 2009-05-07  JPP  - Don't use glass panel for overlays when in design mode. It's too confusing.
 * 2009-05-05  JPP  - Added Scroll event (thanks to Christophe Hosten for the complete patch to implement this)
 *                  - Added Unfocused foreground and background colors (also thanks to Christophe Hosten)
 * 2009-04-29  JPP  - Added SelectedColumn property, which puts a slight tint on that column. Combine
 *                    this with TintSortColumn property and the sort column is automatically tinted.
 *                  - Use an overlay to implement "empty list" msg. Default empty list msg is now prettier.
 * 2009-04-28  JPP  - Fixed bug where DoubleClick events were not triggered when CheckBoxes was true
 * 2009-04-23  JPP  - Fixed various bugs under Vista.
 *                  - Made groups collapsible - Vista only. Thanks to Crustyapplesniffer.
 *                  - Forward events from DropSink to the control itself. This allows handlers to be defined
 *                    within the IDE for drop events
 * 2009-04-16  JPP  - Made several properties localizable.
 * 2009-04-11  JPP  - Correctly renderer checkboxes when RowHeight is non-standard
 * 2009-04-11  JPP  - Implemented overlay architecture, based on CustomDraw scheme.
 *                    This unified drag drop feedback, empty list msgs and overlay images.
 *                  - Added OverlayImage and friends, which allows an image to be drawn
 *                    transparently over the listview
 * 2009-04-10  JPP  - Fixed long-standing annoying flicker on owner drawn virtual lists!
 *                    This means, amongst other things, that grid lines no longer get confused,
 *                    and drag-select no longer flickers.
 * 2009-04-07  JPP  - Calculate edit rectangles more accurately
 * 2009-04-06  JPP  - Double-clicking no longer toggles the checkbox
 *                  - Double-clicking on a checkbox no longer confuses the checkbox
 * 2009-03-16  JPP  - Optimized the build of autocomplete lists
 * v2.1
 * 2009-02-24  JPP  - Fix bug where double-clicking VERY quickly on two different cells
 *                    could give two editors
 *                  - Maintain focused item when rebuilding list (SF #2547060)
 * 2009-02-22  JPP  - Reworked checkboxes so that events are triggered for virtual lists
 * 2009-02-15  JPP  - Added ObjectListView.ConfigureAutoComplete utility method
 * 2009-02-02  JPP  - Fixed bug with AlwaysGroupByColumn where column header clicks would not resort groups.
 * 2009-02-01  JPP  - OLVColumn.CheckBoxes and TriStateCheckBoxes now work.
 * 2009-01-28  JPP  - Complete overhaul of renderers!
 *                       - Use IRenderer
 *                       - Added ObjectListView.ItemRenderer to draw whole items
 * 2009-01-23  JPP  - Simple Checkboxes now work properly
 *                  - Added TriStateCheckBoxes property to control whether the user can
 *                    set the row checkbox to have the Indeterminate value
 *                  - CheckState property is now just a wrapper around the StateImageIndex property
 * 2009-01-20  JPP  - Changed to always draw columns when owner drawn, rather than falling back on DrawDefault.
 *                    This simplified several owner drawn problems
 *                  - Added DefaultRenderer property to help with the above
 *                  - HotItem background color is applied to all cells even when FullRowSelect is false
 *                  - Allow grouping by CheckedAspectName columns
 *                  - Commented out experimental animations. Still needs work.
 * 2009-01-17  JPP  - Added HotItemStyle and UseHotItem to highlight the row under the cursor
 *                  - Added UseCustomSelectionColors property
 *                  - Owner draw mode now honors ForeColor and BackColor settings on the list
 * 2009-01-16  JPP  - Changed to use EditorRegistry rather than hard coding cell editors
 * 2009-01-10  JPP  - Changed to use Equals() method rather than == to compare model objects.
 * v2.0.1
 * 2009-01-08  JPP  - Fixed long-standing "multiple columns generated" problem.
 *                    Thanks to pinkjones for his help with solving this one!
 *                  - Added EnsureGroupVisible()
 * 2009-01-07  JPP  - Made all public and protected methods virtual
 *                  - FinishCellEditing, PossibleFinishCellEditing and CancelCellEditing are now public
 * 2008-12-20  JPP  - Fixed bug with group comparisons when a group key was null (SF#2445761)
 * 2008-12-19  JPP  - Fixed bug with space filling columns and layout events
 *                  - Fixed RowHeight so that it only changes the row height, not the width of the images.
 * v2.0
 * 2008-12-10  JPP  - Handle Backspace key. Resets the seach-by-typing state without delay
 *                  - Made some changes to the column collection editor to try and avoid
 *                    the multiple column generation problem.
 *                  - Updated some documentation
 * 2008-12-07  JPP  - Search-by-typing now works when showing groups
 *                  - Added BeforeSearching and AfterSearching events which are triggered when the user types
 *                    into the list.
 *                  - Added secondary sort information to Before/AfterSorting events
 *                  - Reorganized group sorting code. Now triggers Sorting events.
 *                  - Added GetItemIndexInDisplayOrder()
 *                  - Tweaked in the interaction of the column editor with the IDE so that we (normally)
 *                    don't rely on a hack to find the owning ObjectListView
 *                  - Changed all 'DefaultValue(typeof(Color), "Empty")' to 'DefaultValue(typeof(Color), "")'
 *                    since the first does not given Color.Empty as I thought, but the second does.
 * 2008-11-28  JPP  - Fixed long standing bug with horizontal scrollbar when shrinking the window.
 *                    (thanks to Bartosz Borowik)
 * 2008-11-25  JPP  - Added support for dynamic tooltips
 *                  - Split out comparers and header controls stuff into their own files
 * 2008-11-21  JPP  - Fixed bug where enabling grouping when there was not a sort column would not
 *                    produce a grouped list. Grouping column now defaults to column 0.
 *                  - Preserve selection on virtual lists when sorting
 * 2008-11-20  JPP  - Added ability to search by sort column to ObjectListView. Unified this with
 *                    ability that was already in VirtualObjectListView
 * 2008-11-19  JPP  - Fixed bug in ChangeToFilteredColumns() where DisplayOrder was not always restored correctly.
 * 2008-10-29  JPP  - Event argument blocks moved to directly within the namespace, rather than being
 *                    nested inside ObjectListView class.
 *                  - Removed OLVColumn.CellEditor since it was never used.
 *                  - Marked OLVColumn.AspectGetterAutoGenerated as obsolete (it has not been used for
 *                    several versions now).
 * 2008-10-28  JPP  - SelectedObjects is now an IList, rather than an ArrayList. This allows
 *                    it to accept generic list (eg List<File>).
 * 2008-10-09  JPP  - Support indeterminate checkbox values.
 *                    [BREAKING CHANGE] CheckStateGetter/CheckStatePutter now use CheckState types only.
 *                    BooleanCheckStateGetter and BooleanCheckStatePutter added to ease transition.
 * 2008-10-08  JPP  - Added setFocus parameter to SelectObject(), which allows focus to be set
 *                    at the same time as selecting.
 * 2008-09-27  JPP  - BIG CHANGE: Fissioned this file into separate files for each component
 * 2008-09-24  JPP  - Corrected bug with owner drawn lists where a column 0 with a renderer
 *                    would draw at column 0 even if column 0 was dragged to another position.
 *                  - Correctly handle space filling columns when columns are added/removed
 * 2008-09-16  JPP  - Consistently use try..finally for BeginUpdate()/EndUpdate() pairs
 * 2008-08-24  JPP  - If LastSortOrder is None when adding objects, don't force a resort.
 * 2008-08-22  JPP  - Catch and ignore some problems with setting TopIndex on FastObjectListViews.
 * 2008-08-05  JPP  - In the right-click column select menu, columns are now sorted by display order, rather than alphabetically
 * v1.13
 * 2008-07-23  JPP  - Consistently use copy-on-write semantics with Add/RemoveObject methods
 * 2008-07-10  JPP  - Enable validation on cell editors through a CellEditValidating event.
 *                    (thanks to Artiom Chilaru for the initial suggestion and implementation).
 * 2008-07-09  JPP  - Added HeaderControl.Handle to allow OLV to be used within UserControls.
 *                    (thanks to Michael Coffey for tracking this down).
 * 2008-06-23  JPP  - Split the more generally useful CopyObjectsToClipboard() method
 *                    out of CopySelectionToClipboard()
 * 2008-06-22  JPP  - Added AlwaysGroupByColumn and AlwaysGroupBySortOrder, which
 *                    force the list view to always be grouped by a particular column.
 * 2008-05-31  JPP  - Allow check boxes on FastObjectListViews
 *                  - Added CheckedObject and CheckedObjects properties
 * 2008-05-11  JPP  - Allow selection foreground and background colors to be changed.
 *                    Windows doesn't allow this, so we can only make it happen when owner
 *                    drawing. Set the HighlightForegroundColor and  HighlightBackgroundColor
 *                    properties and then call EnableCustomSelectionColors().
 * v1.12
 * 2008-05-08  JPP  - Fixed bug where the column select menu would not appear if the
 *                    ObjectListView has a context menu installed.
 * 2008-05-05  JPP  - Non detail views can now be owner drawn. The renderer installed for
 *                    primary column is given the chance to render the whole item.
 *                    See BusinessCardRenderer in the demo for an example.
 *                  - BREAKING CHANGE: RenderDelegate now returns a bool to indicate if default
 *                    rendering should be done. Previously returned void. Only important if your
 *                    code used RendererDelegate directly. Renderers derived from BaseRenderer
 *                    are unchanged.
 * 2008-05-03  JPP  - Changed cell editing to use values directly when the values are Strings.
 *                    Previously, values were always handed to the AspectToStringConverter.
 *                  - When editing a cell, tabbing no longer tries to edit the next subitem
 *                    when not in details view!
 * 2008-05-02  JPP  - MappedImageRenderer can now handle a Aspects that return a collection
 *                    of values. Each value will be drawn as its own image.
 *                  - Made AddObjects() and RemoveObjects() work for all flavours (or at least not crash)
 *                  - Fixed bug with clearing virtual lists that has been scrolled vertically
 *                  - Made TopItemIndex work with virtual lists.
 * 2008-05-01  JPP  - Added AddObjects() and RemoveObjects() to allow faster mods to the list
 *                  - Reorganised public properties. Now alphabetical.
 *                  - Made the class ObjectListViewState internal, as it always should have been.
 * v1.11
 * 2008-04-29  JPP  - Preserve scroll position when building the list or changing columns.
 *                  - Added TopItemIndex property. Due to problems with the underlying control, this
 *                    property is not always reliable. See property docs for info.
 * 2008-04-27  JPP  - Added SelectedIndex property.
 *                  - Use a different, more general strategy to handle Invoke(). Removed all delegates
 *                    that were only declared to support Invoke().
 *                  - Check all native structures for 64-bit correctness.
 * 2008-04-25  JPP  - Released on SourceForge.
 * 2008-04-13  JPP  - Added ColumnRightClick event.
 *                  - Made the assembly CLS-compliant. To do this, our cell editors were made internal, and
 *                    the constraint on FlagRenderer template parameter was removed (the type must still
 *                    be an IConvertible, but if it isn't, the error will be caught at runtime, not compile time).
 * 2008-04-12  JPP  - Changed HandleHeaderRightClick() to have a columnIndex parameter, which tells
 *                    exactly which column was right-clicked.
 * 2008-03-31  JPP  - Added SaveState() and RestoreState()
 *                  - When cell editing, scrolling with a mouse wheel now ends the edit operation.
 * v1.10
 * 2008-03-25  JPP  - Added space filling columns. See OLVColumn.FreeSpaceProportion property for details.
 *                    A space filling columns fills all (or a portion) of the width unoccupied by other columns.
 * 2008-03-23  JPP  - Finished tinkering with support for Mono. Compile with conditional compilation symbol 'MONO'
 *                    to enable. On Windows, current problems with Mono:
 *                    - grid lines on virtual lists crashes
 *                    - when grouped, items sometimes are not drawn when any item is scrolled out of view
 *                    - i can't seem to get owner drawing to work
 *                    - when editing cell values, the editing controls always appear behind the listview,
 *                      where they function fine -- the user just can't see them :-)
 * 2008-03-16  JPP  - Added some methods suggested by Chris Marlowe (thanks for the suggestions Chris)
 *                    - ClearObjects()
 *                    - GetCheckedObject(), GetCheckedObjects()
 *                    - GetItemAt() variation that gets both the item and the column under a point
 * 2008-02-28  JPP  - Fixed bug with subitem colors when using OwnerDrawn lists and a RowFormatter.
 * v1.9.1
 * 2008-01-29  JPP  - Fixed bug that caused owner-drawn virtual lists to use 100% CPU
 *                  - Added FlagRenderer to help draw bitwise-OR'ed flag values
 * 2008-01-23  JPP  - Fixed bug (introduced in v1.9) that made alternate row colour with groups not quite right
 *                  - Ensure that DesignerSerializationVisibility.Hidden is set on all non-browsable properties
 *                  - Make sure that sort indicators are shown after changing which columns are visible
 * 2008-01-21  JPP  - Added FastObjectListView
 * v1.9
 * 2008-01-18  JPP  - Added IncrementalUpdate()
 * 2008-01-16  JPP  - Right clicking on column header will allow the user to choose which columns are visible.
 *                    Set SelectColumnsOnRightClick to false to prevent this behaviour.
 *                  - Added ImagesRenderer to draw more than one images in a column
 *                  - Changed the positioning of the empty list m to use all the client area. Thanks to Matze.
 * 2007-12-13  JPP  - Added CopySelectionToClipboard(). Ctrl-C invokes this method. Supports text
 *                    and HTML formats.
 * 2007-12-12  JPP  - Added support for checkboxes via CheckStateGetter and CheckStatePutter properties.
 *                  - Made ObjectListView and OLVColumn into partial classes so that others can extend them.
 * 2007-12-09  JPP  - Added ability to have hidden columns, i.e. columns that the ObjectListView knows
 *                    about but that are not visible to the user. Controlled by OLVColumn.IsVisible.
 *                    Added ColumnSelectionForm to the project to show how it could be used in an application.
 *
 * v1.8
 * 2007-11-26  JPP  - Cell editing fully functional
 * 2007-11-21  JPP  - Added SelectionChanged event. This event is triggered once when the
 *                    selection changes, no matter how many items are selected or deselected (in
 *                    contrast to SelectedIndexChanged which is called once for every row that
 *                    is selected or deselected). Thanks to lupokehl42 (Daniel) for his suggestions and
 *                    improvements on this idea.
 * 2007-11-19  JPP  - First take at cell editing
 * 2007-11-17  JPP  - Changed so that items within a group are not sorted if lastSortOrder == None
 *                  - Only call MakeSortIndicatorImages() if we haven't already made the sort indicators
 *                    (Corrected misspelling in the name of the method too)
 * 2007-11-06  JPP  - Added ability to have secondary sort criteria when sorting
 *                    (SecondarySortColumn and SecondarySortOrder properties)
 *                  - Added SortGroupItemsByPrimaryColumn to allow group items to be sorted by the
 *                    primary column. Previous default was to sort by the grouping column.
 * v1.7
 * No big changes to this version but made to work with ListViewPrinter and released with it.
 *
 * 2007-11-05  JPP  - Changed BaseRenderer to use DrawString() rather than TextRenderer, since TextRenderer
 *                    does not work when printing.
 * v1.6
 * 2007-11-03  JPP  - Fixed some bugs in the rebuilding of DataListView.
 * 2007-10-31  JPP  - Changed to use builtin sort indicators on XP and later. This also avoids alignment
 *                    problems on Vista. (thanks to gravybod for the suggestion and example implementation)
 * 2007-10-21  JPP  - Added MinimumWidth and MaximumWidth properties to OLVColumn.
 *                  - Added ability for BuildList() to preserve selection. Calling BuildList() directly
 *                    tries to preserve selection; calling SetObjects() does not.
 *                  - Added SelectAll() and DeselectAll() methods. Useful for working with large lists.
 * 2007-10-08  JPP  - Added GetNextItem() and GetPreviousItem(), which walk sequentially through the
 *                    listview items, even when the view is grouped.
 *                  - Added SelectedItem property
 * 2007-09-28  JPP  - Optimized aspect-to-string conversion. BuildList() 15% faster.
 *                  - Added empty implementation of RefreshObjects() to VirtualObjectListView since
 *                    RefreshObjects() cannot work on virtual lists.
 * 2007-09-13  JPP  - Corrected bug with custom sorter in VirtualObjectListView (thanks for mpgjunky)
 * 2007-09-07  JPP  - Corrected image scaling bug in DrawAlignedImage() (thanks to krita970)
 * 2007-08-29  JPP  - Allow item count labels on groups to be set per column (thanks to cmarlow for idea)
 * 2007-08-14  JPP  - Major rework of DataListView based on Ian Griffiths's great work
 * 2007-08-11  JPP  - When empty, the control can now draw a "List Empty" m
 *                  - Added GetColumn() and GetItem() methods
 * v1.5
 * 2007-08-03  JPP  - Support animated GIFs in ImageRenderer
 *                  - Allow height of rows to be specified - EXPERIMENTAL!
 * 2007-07-26  JPP  - Optimised redrawing of owner-drawn lists by remembering the update rect
 *                  - Allow sort indicators to be turned off
 * 2007-06-30  JPP  - Added RowFormatter delegate
 *                  - Allow a different label when there is only one item in a group (thanks to cmarlow)
 * v1.4
 * 2007-04-12  JPP  - Allow owner drawn on steriods!
 *                  - Column headers now display sort indicators
 *                  - ImageGetter delegates can now return ints, strings or Images
 *                    (Images are only visible if the list is owner drawn)
 *                  - Added OLVColumn.MakeGroupies to help with group partitioning
 *                  - All normal listview views are now supported
 *                  - Allow dotted aspect names, e.g. Owner.Workgroup.Name (thanks to OlafD)
 *                  - Added SelectedObject and SelectedObjects properties
 * v1.3
 * 2007-03-01  JPP  - Added DataListView
 *                  - Added VirtualObjectListView
 * 					- Added Freeze/Unfreeze capabilities
 *                  - Allowed sort handler to be installed
 *                  - Simplified sort comparisons: handles 95% of cases with only 6 lines of code!
 *                  - Fixed bug with alternative line colors on unsorted lists (thanks to cmarlow)
 * 2007-01-13  JPP  - Fixed bug with lastSortOrder (thanks to Kwan Fu Sit)
 *                  - Non-OLVColumns are no longer allowed
 * 2007-01-04  JPP  - Clear sorter before rebuilding list. 10x faster! (thanks to aaberg)
 *                  - Include GetField in GetAspectByName() so field values can be Invoked too.
 * 					- Fixed subtle bug in RefreshItem() that erased background colors.
 * 2006-11-01  JPP  - Added alternate line colouring
 * 2006-10-20  JPP  - Refactored all sorting comparisons and made it extendable. See ComparerManager.
 *                  - Improved IDE integration
 *                  - Made control DoubleBuffered
 *                  - Added object selection methods
 * 2006-10-13  JPP  Implemented grouping and column sorting
 * 2006-10-09  JPP  Initial version
 *
 * TO DO:
 * - Support undocumented group features: subseted groups, group footer items
 * - Complete custom designer
 *
 * Copyright (C) 2006-2011 Phillip Piper
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU General Public License
 * along with this program.  If not, see <http://www.gnu.org/licenses/>.
 *
 * If you wish to use this code in a closed source application, please contact phillip_piper@bigfoot.com.
 */

using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Serialization.Formatters.Binary;
using System.Windows.Forms;
using System.Windows.Forms.VisualStyles;
using System.Runtime.Serialization.Formatters;

namespace BrightIdeasSoftware
{
    /// <summary>
    /// An ObjectListView is a much easier to use, and much more powerful, version of the ListView.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An ObjectListView automatically populates a ListView control with information taken 
    /// from a given collection of objects. It can do this because each column is configured
    /// to know which bit of the model object (the "aspect") it should be displaying. Columns similarly
    /// understand how to sort the list based on their aspect, and how to construct groups
    /// using their aspect.
    /// </para>
    /// <para>
    /// Aspects are extracted by giving the name of a method to be called or a
    /// property to be fetched. These names can be simple names or they can be dotted
    /// to chain property access e.g. "Owner.Address.Postcode".
    /// Aspects can also be extracted by installing a delegate.
    /// </para>
    /// <para>
    /// An ObjectListView can show a "this list is empty" message when there is nothing to show in the list, 
    /// so that the user knows the control is supposed to be empty.
    /// </para>
    /// <para>
    /// Right clicking on a column header should present a menu which can contain:
    /// commands (sort, group, ungroup); filtering; and column selection. Whether these
    /// parts of the menu appear is controlled by ShowCommandMenuOnRightClick, 
    /// ShowFilterMenuOnRightClick and SelectColumnsOnRightClick respectively.
    /// </para>
    /// <para>
    /// The groups created by an ObjectListView can be configured to include other formatting
    /// information, including a group icon, subtitle and task button. Using some undocumented
    /// interfaces, these groups can even on virtual lists.
    /// </para>
    /// <para>
    /// ObjectListView supports dragging rows to other places, including other application. 
    /// Special support is provide for drops from other ObjectListViews in the same application. 
    /// In many cases, an ObjectListView becomes a full drag source by setting <see cref="IsSimpleDragSource"/> to 
    /// true. Similarly, to accept drops, it is usually enough to set <see cref="IsSimpleDropSink"/> to true, 
    /// and then handle the <see cref="CanDrop"/>  and <see cref="Dropped"/>  events (or the <see cref="ModelCanDrop"/>  and 
    /// <see cref="ModelDropped"/> events, if you only want to handle drops from other ObjectListViews in your application).
    /// </para>
    /// <para>
    /// For these classes to build correctly, the project must have references to these assemblies:
    /// </para>
    /// <list type="bullet">
    /// <item><description>System</description></item>
    /// <item><description>System.Data</description></item>
    /// <item><description>System.Design</description></item>
    /// <item><description>System.Drawing</description></item>
    /// <item><description>System.Windows.Forms (obviously)</description></item>
    /// </list>
    /// </remarks>
    //[Designer("BrightIdeasSoftware.Design.ObjectListViewDesigner")] // Uncomment this if you are feeling brave :)
    public partial class ObjectListView : ListView, ISupportInitialize {
        
        #region Life and death
        
        /// <summary>
        /// Create an ObjectListView
        /// </summary>
        public ObjectListView() {
            this.ColumnClick += new ColumnClickEventHandler(this.HandleColumnClick);
            this.Layout += new LayoutEventHandler(this.HandleLayout);
            this.ColumnWidthChanging += new ColumnWidthChangingEventHandler(this.HandleColumnWidthChanging);
            this.ColumnWidthChanged += new ColumnWidthChangedEventHandler(this.HandleColumnWidthChanged);

            base.View = View.Details;
            this.DoubleBuffered = true; // kill nasty flickers. hiss... me hates 'em
            this.ShowSortIndicators = true;

            // Setup the overlays that will be controlled by the IDE settings
            this.InitializeStandardOverlays();
            this.InitializeEmptyListMsgOverlay();
        }

        /// <summary>
        /// Dispose of any resources this instance has been using
        /// </summary>
        /// <param name="disposing"></param>
        protected override void Dispose(bool disposing) {
            base.Dispose(disposing);

            if (disposing) {
                foreach (GlassPanelForm glassPanel in this.glassPanels) {
                    glassPanel.Unbind();
                    glassPanel.Dispose();
                }
                this.glassPanels.Clear();
            }
        }

        #endregion

        #region Static properties

        /// <summary>
        /// Gets whether the program running on Vista or later?
        /// </summary>
        static public bool IsVistaOrLater {
            get {
                if (!ObjectListView.isVistaOrLater.HasValue)
                    ObjectListView.isVistaOrLater = Environment.OSVersion.Version.Major >= 6;
                return ObjectListView.isVistaOrLater.Value;
            }
        }
        static private bool? isVistaOrLater;

        /// <summary>
        /// Gets whether the program running on Win7 or later?
        /// </summary>
        static public bool IsWin7OrLater {
            get {
                if (!ObjectListView.isWin7OrLater.HasValue) {
                    // For some reason, Win7 is v6.1, not v7.0
                    Version version = Environment.OSVersion.Version;
                    ObjectListView.isWin7OrLater = version.Major > 6 || (version.Major == 6 && version.Minor > 0);
                }
                return ObjectListView.isWin7OrLater.Value;
            }
        }
        static private bool? isWin7OrLater;

        /// <summary>
        /// Gets or sets how what smoothing mode will be applied to graphic operations.
        /// </summary>
        static public System.Drawing.Drawing2D.SmoothingMode SmoothingMode {
            get { return ObjectListView.smoothingMode; }
            set { ObjectListView.smoothingMode = value; }
        }
        static private System.Drawing.Drawing2D.SmoothingMode smoothingMode =
            System.Drawing.Drawing2D.SmoothingMode.HighQuality;

        /// <summary>
        /// Gets or sets how should text be renderered.
        /// </summary>
        static public System.Drawing.Text.TextRenderingHint TextRenderingHint {
            get { return ObjectListView.textRendereringHint; }
            set { ObjectListView.textRendereringHint = value; }
        }
        static private System.Drawing.Text.TextRenderingHint textRendereringHint =
            System.Drawing.Text.TextRenderingHint.SystemDefault;

        /// <summary>
        /// Convert the given enumerable into an ArrayList as efficiently as possible
        /// </summary>
        /// <param name="collection">The source collection</param>
        /// <param name="alwaysCreate">If true, this method will always create a new
        /// collection.</param>
        /// <returns>An ArrayList with the same contents as the given collection.</returns>
        /// <remarks>
        /// <para>When we move to .NET 3.5, we can use LINQ and not need this method.</para>
        /// </remarks>
        public static ArrayList EnumerableToArray(IEnumerable collection, bool alwaysCreate) {
            if (collection == null)
                return new ArrayList();

            if (!alwaysCreate) {
                ArrayList array = collection as ArrayList;
                if (array != null)
                    return array;

                IList iList = collection as IList;
                if (iList != null)
                    return ArrayList.Adapter(iList);
            }

            ICollection iCollection = collection as ICollection;
            if (iCollection != null)
                return new ArrayList(iCollection);

            ArrayList newObjects = new ArrayList();
            foreach (object x in collection)
                newObjects.Add(x);
            return newObjects;
        }

        ///// <summary>
        ///// Decide if the given enumerable is empty
        ///// </summary>
        ///// <param name="collection">The source collection</param>
        ///// <returns>True if the given enumerable is empty</returns>
        ///// <remarks>
        ///// <para>When we move to .NET 3.5, we can use LINQ and not need this method.</para>
        ///// </remarks>
        //public static bool IsEnumerableEmpty(IEnumerable collection) {
        //    if (collection == null)
        //        return true;

        //    IEnumerator enumerator = collection.GetEnumerator();
        //    return !enumerator.MoveNext();
        //}

        #endregion

        #region Public properties

        /// <summary>
        /// Get or set all the columns that this control knows about.
        /// Only those columns where IsVisible is true will be seen by the user.
        /// </summary>
        /// <remarks>
        /// <para>
        /// If you want to add new columns programmatically, add them to
        /// AllColumns and then call RebuildColumns(). Normally, you do not have to
        /// deal with this property directly. Just use the IDE.
        /// </para>
        /// <para>If you do add or remove columns from the AllColumns collection,
        /// you have to call RebuildColumns() to make those changes take effect.</para>
        /// </remarks>
        [Browsable(false),
        DesignerSerializationVisibility(DesignerSerializationVisibility.Content)]
        public virtual List<OLVColumn> AllColumns {
            get { return this.allColumns; }
            set { this.allColumns = value ?? new List<OLVColumn>(); }
        }
        private List<OLVColumn> allColumns = new List<OLVColumn>();

        /// <summary>
        /// Gets or sets the background color of every second row 
        /// </summary>
        [Category("ObjectListView"),
         Description("If using alternate colors, what color should the background of alterate rows be?"),
         DefaultValue(typeof(Color), "")]
        public Color AlternateRowBackColor {
            get { return alternateRowBackColor; }
            set { alternateRowBackColor = value; }
        }
        private Color alternateRowBackColor = Color.Empty;

        /// <summary>
        /// Gets the alternate row background color that has been set, or the default color
        /// </summary>
        [Browsable(false)]
        public virtual Color AlternateRowBackColorOrDefault {
            get {
                return this.alternateRowBackColor == Color.Empty ? Color.LemonChiffon : this.alternateRowBackColor;
            }
        }

        /// <summary>
        /// This property forces the ObjectListView to always group items by the given column.
        /// </summary>
        [Browsable(false),
        DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public virtual OLVColumn AlwaysGroupByColumn {
            get { return alwaysGroupByColumn; }
            set { alwaysGroupByColumn = value; }
        }
        private OLVColumn alwaysGroupByColumn;

        /// <summary>
        /// If AlwaysGroupByColumn is not null, this property will be used to decide how
        /// those groups are sorted. If this property has the value SortOrder.None, then
        /// the sort order will toggle according to the users last header click.
        /// </summary>
        [Browsable(false),
        DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public virtual SortOrder AlwaysGroupBySortOrder {
            get { return alwaysGroupBySortOrder; }
            set { alwaysGroupBySortOrder = value; }
        }
        private SortOrder alwaysGroupBySortOrder = SortOrder.None;

        /// <summary>
        /// Give access to the image list that is actually being used by the control
        /// </summary>
        /// <remarks>
        /// Normally, it is preferable to use SmallImageList. Only use this property
        /// if you know exactly what you are doing.
        /// </remarks>
        [Browsable(false),
        DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public virtual ImageList BaseSmallImageList {
            get { return base.SmallImageList; }
            set { base.SmallImageList = value; }
        }

        /// <summary>
        /// How does the user indicate that they want to edit a cell?
        /// None means that the listview cannot be edited.
        /// </summary>
        /// <remarks>Columns can also be marked as editable.</remarks>
        [Category("ObjectListView"),
        Description("How does the user indicate that they want to edit a cell?"),
        DefaultValue(CellEditActivateMode.None)]
        public virtual CellEditActivateMode CellEditActivation {
            get { return cellEditActivation; }
            set { 
                cellEditActivation = value;
                if (this.Created)
                    this.Invalidate();
            }
        }
        private CellEditActivateMode cellEditActivation = CellEditActivateMode.None;

        /// <summary>
        /// Gets or sets the engine that will handle key presses during a cell edit operation.
        /// Settings this to null will reset it to default value.
        /// </summary>
        [Browsable(false),
         DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public CellEditKeyEngine CellEditKeyEngine {
            get {
                if (this.cellEditKeyEngine == null)
                    this.cellEditKeyEngine = new CellEditKeyEngine();
                return this.cellEditKeyEngine;
            }
            set { this.cellEditKeyEngine = value; }
        }
        private CellEditKeyEngine cellEditKeyEngine;

        /// <summary>
        /// Gets the control that is currently being used for editing a cell.
        /// </summary>
        /// <remarks>This will obviously be null if no cell is being edited.</remarks>
        [Browsable(false)]
        public Control CellEditor {
            get {
                return this.cellEditor;
            }
        }

        /// <summary>
        /// Gets or sets the behaviour of the Tab key when editing a cell on the left or right
        /// edge of the control. If this is false (the default), pressing Tab will wrap to the other side
        /// of the same row. If this is true, pressing Tab when editing the right most cell will advance 
        /// to the next row 
        /// and Shift-Tab when editing the left-most cell will change to the previous row.
        /// </summary>
        [Category("ObjectListView"),
        Description("Should Tab/Shift-Tab change rows while cell editing?"),
        DefaultValue(false)]
        public virtual bool CellEditTabChangesRows {
            get { return cellEditTabChangesRows; }
            set {
                cellEditTabChangesRows = value;
                if (cellEditTabChangesRows) {
                    this.CellEditKeyEngine.SetKeyBehaviour(Keys.Tab, CellEditCharacterBehaviour.ChangeColumnRight, CellEditAtEdgeBehaviour.ChangeRow);
                    this.CellEditKeyEngine.SetKeyBehaviour(Keys.Tab|Keys.Shift, CellEditCharacterBehaviour.ChangeColumnLeft, CellEditAtEdgeBehaviour.ChangeRow);
                } else {
                    this.CellEditKeyEngine.SetKeyBehaviour(Keys.Tab, CellEditCharacterBehaviour.ChangeColumnRight, CellEditAtEdgeBehaviour.Wrap);
                    this.CellEditKeyEngine.SetKeyBehaviour(Keys.Tab | Keys.Shift, CellEditCharacterBehaviour.ChangeColumnLeft, CellEditAtEdgeBehaviour.Wrap);
                }
            }
        }
        private bool cellEditTabChangesRows = false;

        /// <summary>
        /// Gets or sets the behaviour of the Enter keys while editing a cell.
        /// If this is false (the default), pressing Enter will simply finish the editing operation.
        /// If this is true, Enter will finish the edit operation and start a new edit operation
        /// on the cell below the current cell, wrapping to the top of the next row when at the bottom cell.
        /// </summary>
        [Category("ObjectListView"),
        Description("Should Enter change rows while cell editing?"),
        DefaultValue(false)]
        public virtual bool CellEditEnterChangesRows {
            get { return cellEditEnterChangesRows; }
            set {
                cellEditEnterChangesRows = value;
                if (cellEditEnterChangesRows) {
                    this.CellEditKeyEngine.SetKeyBehaviour(Keys.Enter, CellEditCharacterBehaviour.ChangeRowDown, CellEditAtEdgeBehaviour.ChangeColumn);
                    this.CellEditKeyEngine.SetKeyBehaviour(Keys.Enter | Keys.Shift, CellEditCharacterBehaviour.ChangeRowUp, CellEditAtEdgeBehaviour.ChangeColumn);
                } else {
                    this.CellEditKeyEngine.SetKeyBehaviour(Keys.Enter, CellEditCharacterBehaviour.EndEdit, CellEditAtEdgeBehaviour.EndEdit);
                    this.CellEditKeyEngine.SetKeyBehaviour(Keys.Enter | Keys.Shift, CellEditCharacterBehaviour.EndEdit, CellEditAtEdgeBehaviour.EndEdit);
                }
            }
        }
        private bool cellEditEnterChangesRows = false;

        /// <summary>
        /// Gets the tool tip control that shows tips for the cells
        /// </summary>
        [Browsable(false)]
        public ToolTipControl CellToolTip {
            get {
                if (this.cellToolTip == null) {
                    this.CreateCellToolTip();
                }
                return this.cellToolTip;
            }
        }
        private ToolTipControl cellToolTip;

        /// <summary>
        /// Should this list show checkboxes?
        /// </summary>
        public new bool CheckBoxes {
            get {
                return base.CheckBoxes;
            }
            set {
                base.CheckBoxes = value;

                // Initialize the state image list so we can display indetermined values.
                this.InitializeStateImageList();
            }
        }

        /// <summary>
        /// Return the model object of the row that is checked or null if no row is checked
        /// or more than one row is checked
        /// </summary>
        [Browsable(false),
        DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public virtual Object CheckedObject {
            get {
                IList checkedObjects = this.CheckedObjects;
                if (checkedObjects.Count == 1)
                    return checkedObjects[0];
                else
                    return null;
            }
            set {
                this.CheckedObjects = new ArrayList(new Object[] { value });
            }
        }

        /// <summary>
        /// Get or set the collection of model objects that are checked.
        /// When setting this property, any row whose model object isn't
        /// in the given collection will be unchecked. Setting to null is
        /// equivilent to unchecking all.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This property returns a simple collection. Changes made to the returned
        /// collection do NOT affect the list. This is different to the behaviour of
        /// CheckedIndicies collection.
        /// </para>
        /// <para>
        /// .NET's CheckedItems property is not helpful. It is just a short-hand for
        /// iterating through the list looking for items that are checked.
        /// </para>
        /// <para>
        /// The performance of the get method is O(n), where n is the number of items
        /// in the control. The performance of the set method is
        /// O(n + m) where m is the number of objects being checked. Be careful on long lists.
        /// </para>
        /// </remarks>
        [Browsable(false),
         DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public virtual IList CheckedObjects {
            get {
                ArrayList objects = new ArrayList();
                if (this.CheckBoxes) {
                    for (int i = 0; i < this.GetItemCount(); i++) {
                        OLVListItem olvi = this.GetItem(i);
                        if (olvi.CheckState == CheckState.Checked)
                            objects.Add(olvi.RowObject);
                    }
                }
                return objects;
            }
            set {
                if (!this.CheckBoxes)
                    return;

                // Set up an efficient way of testing for the presence of a particular model
                Hashtable table = new Hashtable(this.GetItemCount());
                if (value != null) {
                    foreach (object x in value)
                        table[x] = true;
                }

                foreach (Object x in this.Objects) {
                    this.SetObjectCheckedness(x, table.ContainsKey(x) ? CheckState.Checked : CheckState.Unchecked);
                }
            }
        }

        /// <summary>
        /// Gets or sets the checked objects from an enumerable.
        /// </summary>
        /// <remarks>
        /// Useful for checking all objects in the list.
        /// </remarks>
        /// <example>
        /// this.olv1.CheckedObjectsEnumerable = this.olv1.Objects;
        /// </example>
        [Browsable(false),
        DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public virtual IEnumerable CheckedObjectsEnumerable {
            get {
                return this.CheckedObjects;
            }
            set {
                this.CheckedObjects = ObjectListView.EnumerableToArray(value, true);
            }
        }

        /// <summary>
        /// Gets Columns for this list. We hide the original so we can associate
        /// a specialised editor with it.
        /// </summary>
        [Editor("BrightIdeasSoftware.Design.OLVColumnCollectionEditor", "System.Drawing.Design.UITypeEditor")]
        new public ListView.ColumnHeaderCollection Columns {
            get {
                return base.Columns;
            }
        }

        /// <summary>
        /// Get/set the list of columns that should be used when the list switches to tile view.
        /// </summary>
        [Browsable(false),
        Obsolete("Use GetFilteredColumns() and OLVColumn.IsTileViewColumn instead"),
        DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public List<OLVColumn> ColumnsForTileView {
            get { return this.GetFilteredColumns(View.Tile); }
        }

        /// <summary>
        /// Return the visible columns in the order they are displayed to the user
        /// </summary>
        [Browsable(false)]
        public virtual List<OLVColumn> ColumnsInDisplayOrder {
            get {
                OLVColumn[] columnsInDisplayOrder = new OLVColumn[this.Columns.Count];
                foreach (OLVColumn col in this.Columns) {
                    columnsInDisplayOrder[col.DisplayIndex] = col;
                }
                return new List<OLVColumn>(columnsInDisplayOrder);
            }
        }


        /// <summary>
        /// Get the area of the control that shows the list, minus any header control
        /// </summary>
        [Browsable(false)]
        public Rectangle ContentRectangle {
            get {
                Rectangle r = this.ClientRectangle;

                // If the listview has a header control, remove the header from the control area
                if (this.View == View.Details && this.HeaderControl != null) {
                    Rectangle hdrBounds = new Rectangle();
                    NativeMethods.GetClientRect(this.HeaderControl.Handle, ref hdrBounds);
                    r.Y = hdrBounds.Height;
                    r.Height = r.Height - hdrBounds.Height;
                }

                return r;
            }
        }

        /// <summary>
        /// Gets or sets if the selected rows should be copied to the clipboard when the user presses Ctrl-C
        /// </summary>
        [Category("ObjectListView"),
        Description("Should the control copy the selection to the clipboard when the user presses Ctrl-C?"),
        DefaultValue(true)]
        public virtual bool CopySelectionOnControlC {
            get { return copySelectionOnControlC; }
            set { copySelectionOnControlC = value; }
        }
        private bool copySelectionOnControlC = true;


        /// <summary>
        /// Gets or sets whether the Control-C copy to clipboard functionality should use
        /// the installed DragSource to create the data object that is placed onto the clipboard.
        /// </summary>
        /// <remarks>This is normally what is desired, unless a custom DragSource is installed 
        /// that does some very specialized drag-drop behaviour.</remarks>
        [Category("ObjectListView"),
        Description("Should the Ctrl-C copy process use the DragSource to create the Clipboard data object?"),
        DefaultValue(true)]
        public bool CopySelectionOnControlCUsesDragSource {
            get { return this.copySelectionOnControlCUsesDragSource; }
            set { this.copySelectionOnControlCUsesDragSource = value; }
        }
        private bool copySelectionOnControlCUsesDragSource = true;

        /// <summary>
        /// Gets the list of decorations that will be drawn the ListView
        /// </summary>
        /// <remarks>
        /// <para>
        /// Do not modify the contents of this list directly. Use the AddDecoration() and RemoveDecoration() methods.
        /// </para>
        /// <para>
        /// A decoration scrolls with the list contents. An overlay is fixed in place.
        /// </para>
        /// </remarks>
        [Browsable(false)]
        protected IList<IDecoration> Decorations {
            get { return this.decorations; }
        }
        private List<IDecoration> decorations = new List<IDecoration>();

        /// <summary>
        /// When owner drawing, this renderer will draw columns that do not have specific renderer
        /// given to them
        /// </summary>
        /// <remarks>If you try to set this to null, it will revert to a BaseRenderer</remarks>
        [Browsable(false),
         DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public IRenderer DefaultRenderer {
            get { return this.defaultRenderer; }
            set { this.defaultRenderer = value ?? new BaseRenderer(); }
        }
        private IRenderer defaultRenderer = new BaseRenderer();

        /// <summary>
        /// Gets or sets the object that controls how drags start from this control
        /// </summary>
        [Browsable(false),
         DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public IDragSource DragSource {
            get { return this.dragSource; }
            set { this.dragSource = value; }
        }
        private IDragSource dragSource;

        /// <summary>
        /// Gets or sets the object that controls how drops are accepted and processed
        /// by this ListView.
        /// </summary>
        /// <remarks>
        /// <para>
        /// If the given sink is an instance of SimpleDropSink, then events from the drop sink
        /// will be automatically forwarded to the ObjectListView (which means that handlers
        /// for those event can be configured within the IDE).
        /// </para>
        /// <para>If this is set to null, the control will not accept drops.</para>
        /// </remarks>
        [Browsable(false),
         DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public IDropSink DropSink {
            get { return this.dropSink; }
            set {
                if (this.dropSink == value)
                    return;

                // Stop listening for events on the old sink
                SimpleDropSink oldSink = this.dropSink as SimpleDropSink;
                if (oldSink != null) {
                    oldSink.CanDrop -= new EventHandler<OlvDropEventArgs>(dropSink_CanDrop);
                    oldSink.Dropped -= new EventHandler<OlvDropEventArgs>(dropSink_Dropped);
                    oldSink.ModelCanDrop -= new EventHandler<ModelDropEventArgs>(dropSink_ModelCanDrop);
                    oldSink.ModelDropped -= new EventHandler<ModelDropEventArgs>(dropSink_ModelDropped);
                }

                this.dropSink = value;
                this.AllowDrop = (value != null);
                if (this.dropSink != null)
                    this.dropSink.ListView = this;

                // Start listening for events on the new sink
                SimpleDropSink newSink = value as SimpleDropSink;
                if (newSink != null) {
                    newSink.CanDrop += new EventHandler<OlvDropEventArgs>(dropSink_CanDrop);
                    newSink.Dropped += new EventHandler<OlvDropEventArgs>(dropSink_Dropped);
                    newSink.ModelCanDrop += new EventHandler<ModelDropEventArgs>(dropSink_ModelCanDrop);
                    newSink.ModelDropped += new EventHandler<ModelDropEventArgs>(dropSink_ModelDropped);
                }
            }
        }
        private IDropSink dropSink;

        // Forward events from the drop sink to the control itself
        void dropSink_CanDrop(object sender, OlvDropEventArgs e) { this.OnCanDrop(e); }
        void dropSink_Dropped(object sender, OlvDropEventArgs e) { this.OnDropped(e); }
        void dropSink_ModelCanDrop(object sender, ModelDropEventArgs e) { this.OnModelCanDrop(e); }
        void dropSink_ModelDropped(object sender, ModelDropEventArgs e) { this.OnModelDropped(e); }

        /// <summary>
        /// This registry decides what control should be used to edit what cells, based
        /// on the type of the value in the cell.
        /// </summary>
        /// <see cref="EditorRegistry"/>
        /// <remarks>All instances of ObjectListView share the same editor registry.</remarks>
        static public EditorRegistry EditorRegistry = new EditorRegistry();

        /// <summary>
        /// Gets or sets the text that should be shown when there are no items in this list view.
        /// </summary>
        /// <remarks>If the EmptyListMsgOverlay has been changed to something other than a TextOverlay,
        /// this property does nothing</remarks>
        [Category("ObjectListView"),
         Description("When the list has no items, show this message in the control"),
         DefaultValue(null),
         Localizable(true)]
        public virtual String EmptyListMsg {
            get {
                TextOverlay overlay = this.EmptyListMsgOverlay as TextOverlay;
                if (overlay == null)
                    return null;
                else
                    return overlay.Text;
            }
            set {
                TextOverlay overlay = this.EmptyListMsgOverlay as TextOverlay;
                if (overlay != null) {
                    overlay.Text = value;
                    this.Invalidate();
                }
            }
        }

        /// <summary>
        /// Gets or sets the font in which the List Empty message should be drawn
        /// </summary>
        /// <remarks>If the EmptyListMsgOverlay has been changed to something other than a TextOverlay,
        /// this property does nothing</remarks>
        [Category("ObjectListView"),
        Description("What font should the 'list empty' message be drawn in?"),
        DefaultValue(null)]
        public virtual Font EmptyListMsgFont {
            get {
                TextOverlay overlay = this.EmptyListMsgOverlay as TextOverlay;
                if (overlay == null)
                    return null;
                else
                    return overlay.Font;
            }
            set {
                TextOverlay overlay = this.EmptyListMsgOverlay as TextOverlay;
                if (overlay != null)
                    overlay.Font = value;
            }
        }

        /// <summary>
        /// Return the font for the 'list empty' message or a reasonable default
        /// </summary>
        [Browsable(false)]
        public virtual Font EmptyListMsgFontOrDefault {
            get {
               return this.EmptyListMsgFont ?? new Font("Tahoma", 14);
            }
        }

        /// <summary>
        /// Gets or sets the overlay responsible for drawing the List Empty msg.
        /// </summary>
        [Browsable(false),
         DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public virtual IOverlay EmptyListMsgOverlay {
            get { return this.emptyListMsgOverlay; }
            set {
                if (this.emptyListMsgOverlay != value) {
                    this.emptyListMsgOverlay = value;
                    this.Invalidate();
                }
            }
        }
        private IOverlay emptyListMsgOverlay;

        /// <summary>
        /// Gets the collection of objects that survive any filtering that may be in place.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This collection is the result of filtering the current list of objects. 
        /// It is not a snapshot of the filtered list that was last used to build the control. 
        /// </para>
        /// <para>
        /// Normal warnings apply when using this with virtual lists. It will work, but it
        /// may take a while.
        /// </para>
        /// </remarks>
        [Browsable(false),
         DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        virtual public IEnumerable FilteredObjects {
            get {
                if (this.IsFiltering)
                    return this.FilterObjects(this.Objects, this.ModelFilter, this.ListFilter);
                else
                    return this.Objects;
            }
        }

        /// <summary>
        /// Gets or sets the strategy object that will be used to build the Filter menu
        /// </summary>
        /// <remarks>If this is null, no filter menu will be built.</remarks>
        [Browsable(false),
        DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public FilterMenuBuilder FilterMenuBuildStrategy {
            get { return filterMenuBuilder; }
            set { filterMenuBuilder = value; }
        }
        private FilterMenuBuilder filterMenuBuilder = new FilterMenuBuilder();

        /// <summary>
        /// Get or set whether or not the listview is frozen. When the listview is
        /// frozen, it will not update itself.
        /// </summary>
        /// <remarks><para>The Frozen property is similar to the methods Freeze()/Unfreeze()
        /// except that changes to the Frozen property do not nest.</para></remarks>
        /// <example>objectListView1.Frozen = false; // unfreeze the control regardless of the number of Freeze() calls
        /// </example>
        [Browsable(false),
         DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public virtual bool Frozen {
            get { return freezeCount > 0; }
            set {
                if (value)
                    Freeze();
                else if (freezeCount > 0) {
                    freezeCount = 1;
                    Unfreeze();
                }
            }
        }
        private int freezeCount;

        /// <summary>
        /// Hide the Groups collection so it's not visible in the Properties grid.
        /// </summary>
        [Browsable(false),
         DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        new public ListViewGroupCollection Groups {
            get { return base.Groups; }
        }

        /// <summary>
        /// Gets or sets the image list from which group header will take their images
        /// </summary>
        /// <remarks>If this is not set, then group headers will not show any images.</remarks>
        [Category("ObjectListView"),
         Description("The image list from which group header will take their images"),
         DefaultValue(null)]
        public ImageList GroupImageList {
            get { return this.groupImageList; }
            set {
                this.groupImageList = value;
                if (this.Created) {
                    int x = NativeMethods.SetGroupImageList(this, value);
                }
            }
        }
        private ImageList groupImageList;

        /// <summary>
        /// Gets how the group label should be formatted when a group is empty or
        /// contains more than one item
        /// </summary>
        /// <remarks>
        /// The given format string must have two placeholders:
        /// <list type="bullet">
        /// <item><description>{0} - the original group title</description></item>
        /// <item><description>{1} - the number of items in the group</description></item>
        /// </list>
        /// </remarks>
        /// <example>"{0} [{1} items]"</example>
        [Category("ObjectListView"),
         Description("The format to use when suffixing item counts to group titles"),
         DefaultValue(null),
         Localizable(true)]
        public virtual string GroupWithItemCountFormat {
            get { return groupWithItemCountFormat; }
            set { groupWithItemCountFormat = value; }
        }
        private string groupWithItemCountFormat;

        /// <summary>
        /// Return this.GroupWithItemCountFormat or a reasonable default
        /// </summary>
        [Browsable(false)]
        public virtual string GroupWithItemCountFormatOrDefault {
            get {
                if (String.IsNullOrEmpty(this.GroupWithItemCountFormat))
                    return "{0} [{1} items]";
                else
                    return this.GroupWithItemCountFormat;
            }
        }

        /// <summary>
        /// Gets how the group label should be formatted when a group contains only a single item
        /// </summary>
        /// <remarks>
        /// The given format string must have two placeholders:
        /// <list type="bullet">
        /// <item><description>{0} - the original group title</description></item>
        /// <item><description>{1} - the number of items in the group (always 1)</description></item>
        /// </list>
        /// </remarks>
        /// <example>"{0} [{1} item]"</example>
        [Category("ObjectListView"),
         Description("The format to use when suffixing item counts to group titles"),
         DefaultValue(null),
         Localizable(true)]
        public virtual string GroupWithItemCountSingularFormat {
            get { return groupWithItemCountSingularFormat; }
            set { groupWithItemCountSingularFormat = value; }
        }
        private string groupWithItemCountSingularFormat;

        /// <summary>
        /// Gets GroupWithItemCountSingularFormat or a reasonable default
        /// </summary>
        [Browsable(false)]
        public virtual string GroupWithItemCountSingularFormatOrDefault {
            get {
                if (String.IsNullOrEmpty(this.GroupWithItemCountSingularFormat))
                    return "{0} [{1} item]";
                else
                    return this.GroupWithItemCountSingularFormat;
            }
        }

        /// <summary>
        /// Gets or sets whether or not the groups in this ObjectListView should be collapsible.
        /// </summary>
        /// <remarks>
        /// This feature only works under Vista and later.
        /// </remarks>
        [Browsable(true),
         Category("ObjectListView"),
         Description("Should the groups in this control be collapsible (Vista and later only)."),
         DefaultValue(true)]
        public bool HasCollapsibleGroups {
            get { return hasCollapsibleGroups; }
            set { hasCollapsibleGroups = value; }
        }
        private bool hasCollapsibleGroups = true;

        /// <summary>
        /// Does this listview have a message that should be drawn when the list is empty?
        /// </summary>
        [Browsable(false)]
        public virtual bool HasEmptyListMsg {
            get { return !String.IsNullOrEmpty(this.EmptyListMsg); }
        }

        /// <summary>
        /// Get whether there are any overlays to be drawn
        /// </summary>
        [Browsable(false)]
        public bool HasOverlays {
            get {
                return (this.Overlays.Count > 2 ||
                    this.imageOverlay.Image != null ||
                    !String.IsNullOrEmpty(this.textOverlay.Text));
            }
        }

        /// <summary>
        /// Gets the header control for the ListView
        /// </summary>
        [Browsable(false)]
        public HeaderControl HeaderControl {
            get {
                if (this.headerControl == null)
                    this.headerControl = new HeaderControl(this);
                return this.headerControl;
            }
        }
        private HeaderControl headerControl;

        /// <summary>
        /// Gets or sets the font in which the text of the column headers will be drawn
        /// </summary>
        /// <remarks>Individual columns can override this through their HeaderFormatStyle property.</remarks>
        [DefaultValue(null)]
        [Browsable(false)]
        [Obsolete("Use a HeaderFormatStyle instead", false)]
        public Font HeaderFont {
            get { return this.HeaderFormatStyle == null ? null : this.HeaderFormatStyle.Normal.Font; }
            set {
                if (value == null && this.HeaderFormatStyle == null)
                    return;

                if (this.HeaderFormatStyle == null)
                    this.HeaderFormatStyle = new HeaderFormatStyle();

                this.HeaderFormatStyle.SetFont(value);
            }
        }

        /// <summary>
        /// Gets or sets the style that will be used to draw the columm headers of the listview
        /// </summary>
        /// <remarks>
        /// <para>
        /// This is only used when HeaderUsesThemes is false.
        /// </para>
        /// <para>
        /// Individual columns can override this through their HeaderFormatStyle property.
        /// </para>
        /// </remarks>
        [Category("ObjectListView"),
         Description("What style will be used to draw the control's header"),
         DefaultValue(null)]
        public HeaderFormatStyle HeaderFormatStyle {
            get { return this.headerFormatStyle; }
            set { this.headerFormatStyle = value; }
        }
        private HeaderFormatStyle headerFormatStyle;

        /// <summary>
        /// Gets or sets the maximum height of the header. -1 means no maximum.
        /// </summary>
        [Category("ObjectListView"),
         Description("What is the maximum height of the header? -1 means no maximum"),
         DefaultValue(-1)]
        public int HeaderMaximumHeight {
            get { return headerMaximumHeight; }
            set { headerMaximumHeight = value; }
        }
        private int headerMaximumHeight = -1;

        /// <summary>
        /// Gets or sets whether the header will be drawn strictly according to the OS's theme. 
        /// </summary>
        /// <remarks>
        /// <para>
        /// If this is set to true, the header will be rendered completely by the system, without
        /// any of ObjectListViews fancy processing -- no images in header, no filter indicators,
        /// no word wrapping, no header styling.
        /// </para>
        /// <para>If this is set to false, ObjectListView will render the header as it thinks best.
        /// If no special features are required, then ObjectListView will delegate rendering to the OS.
        /// Otherwise, ObjectListView will draw the header according to the configuration settings.
        /// </para>
        /// <para>
        /// The effect of not being themed will be different from OS to OS. At
        /// very least, the sort indicator will not be standard. 
        /// </para>
        /// </remarks>
        [Category("ObjectListView"),
         Description("Will the column headers be drawn strictly according to OS theme?"),
         DefaultValue(true)]
        public bool HeaderUsesThemes {
            get { return this.headerUsesThemes; }
            set { this.headerUsesThemes = value; }
        }
        private bool headerUsesThemes = true;

        /// <summary>
        /// Gets or sets the whether the text in the header will be word wrapped.
        /// </summary>
        /// <remarks>
        /// <para>THIS FEATURE IS EXPERIMENTAL (August 2009)</para>
        /// <para>Line breaks will be applied between words. Words that are too long
        /// will still be ellipsed.</para>
        /// <para>
        /// As with all settings that make the header look different, HeaderUsesThemes must be set to false, otherwise
        /// the OS will be responsible for drawing the header, and it does not allow word wrapped text.
        /// </para>
        /// </remarks>
        [Category("ObjectListView"),
         Description("Will the text of the column headers be word wrapped?"),
         DefaultValue(false)]
        public bool HeaderWordWrap {
            get { return this.headerWordWrap; }
            set {
                this.headerWordWrap = value;
                if (this.headerControl != null)
                    this.headerControl.WordWrap = value;
            }
        }
        private bool headerWordWrap;

        /// <summary>
        /// Gets the tool tip that shows tips for the column headers
        /// </summary>
        [Browsable(false)]
        public ToolTipControl HeaderToolTip {
            get {
                return this.HeaderControl.ToolTip;
            }
        }

        /// <summary>
        /// Gets the index of the row that the mouse is currently over
        /// </summary>
        [Browsable(false),
         DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public virtual int HotRowIndex {
            get { return this.hotRowIndex; }
            protected set { this.hotRowIndex = value; }
        }
        private int hotRowIndex;

        /// <summary>
        /// Gets the index of the subitem that the mouse is currently over
        /// </summary>
        [Browsable(false),
         DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public virtual int HotColumnIndex {
            get { return this.hotColumnIndex; }
            protected set { this.hotColumnIndex = value; }
        }
        private int hotColumnIndex;

        /// <summary>
        /// Gets the part of the item/subitem that the mouse is currently over
        /// </summary>
        [Browsable(false),
         DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public virtual HitTestLocation HotCellHitLocation {
            get { return this.hotCellHitLocation; }
            protected set { this.hotCellHitLocation = value; }
        }
        private HitTestLocation hotCellHitLocation;

        /// <summary>
        /// The index of the item that is 'hot', i.e. under the cursor. -1 means no item.
        /// </summary>
        [Browsable(false),
         Obsolete("Use HotRowIndex instead", false)]
        public virtual int HotItemIndex {
            get { return this.HotRowIndex; }
        }

        /// <summary>
        /// What sort of formatting should be applied to the row under the cursor?
        /// </summary>
        /// <remarks>This only takes effect when UseHotItem is true.</remarks>
        [Category("ObjectListView"),
         Description("How should the row under the cursor be highlighted"),
         DefaultValue(null)]
        public virtual HotItemStyle HotItemStyle {
            get { return this.hotItemStyle; }
            set {
                if (this.HotItemStyle != null)
                    this.RemoveOverlay(this.HotItemStyle.Overlay);
                this.hotItemStyle = value;
                if (this.HotItemStyle != null)
                    this.AddOverlay(this.HotItemStyle.Overlay);
            }
        }
        private HotItemStyle hotItemStyle;

        /// <summary>
        /// What sort of formatting should be applied to hyperlinks?
        /// </summary>
        [Category("ObjectListView"),
         Description("How should hyperlinks be drawn"),
         DefaultValue(null)]
        public virtual HyperlinkStyle HyperlinkStyle {
            get { return this.hyperlinkStyle; }
            set { this.hyperlinkStyle = value; }
        }
        private HyperlinkStyle hyperlinkStyle;

        /// <summary>
        /// What color should be used for the background of selected rows?
        /// </summary>
        /// <remarks>Windows does not give the option of changing the selection background.
        /// So the control has to be owner drawn to see the result of this setting.
        /// Setting UseCustomSelectionColors = true will do this for you.</remarks>
        [Category("ObjectListView"),
         Description("The background foregroundColor of selected rows when the control is owner drawn"),
         DefaultValue(typeof(Color), "")]
        public virtual Color HighlightBackgroundColor {
            get { return highlightBackgroundColor; }
            set { highlightBackgroundColor = value; }
        }
        private Color highlightBackgroundColor = Color.Empty;

        /// <summary>
        /// Return the color should be used for the background of selected rows or a reasonable default
        /// </summary>
        [Browsable(false)]
        public virtual Color HighlightBackgroundColorOrDefault {
            get {
                if (this.HighlightBackgroundColor.IsEmpty)
                    return SystemColors.Highlight;
                else
                    return this.HighlightBackgroundColor;
            }
        }

        /// <summary>
        /// What color should be used for the foreground of selected rows?
        /// </summary>
        /// <remarks>Windows does not give the option of changing the selection foreground (text color).
        /// So the control has to be owner drawn to see the result of this setting.
        /// Setting UseCustomSelectionColors = true will do this for you.</remarks>
        [Category("ObjectListView"),
         Description("The foreground foregroundColor of selected rows when the control is owner drawn"),
         DefaultValue(typeof(Color), "")]
        public virtual Color HighlightForegroundColor {
            get { return highlightForegroundColor; }
            set { highlightForegroundColor = value; }
        }
        private Color highlightForegroundColor = Color.Empty;

        /// <summary>
        /// Return the color should be used for the foreground of selected rows or a reasonable default
        /// </summary>
        [Browsable(false)]
        public virtual Color HighlightForegroundColorOrDefault {
            get {
                if (this.HighlightForegroundColor.IsEmpty)
                    return SystemColors.HighlightText;
                else
                    return this.HighlightForegroundColor;
            }
        }

        /// <summary>
        /// Gets or sets whether or not hidden columns should be included in the text representation
        /// of rows that are copied or dragged to another application. If this is false (the default),
        /// only visible columns will be included.
        /// </summary>
        [Category("ObjectListView"),
        Description("When rows are copied or dragged, will data in hidden columns be included in the text? If this is false, only visible columns will be included."),
        DefaultValue(false)]
        public virtual bool IncludeHiddenColumnsInDataTransfer {
            get { return includeHiddenColumnsInDataTransfer; }
            set { includeHiddenColumnsInDataTransfer = value; }
        }
        private bool includeHiddenColumnsInDataTransfer = false;

        /// <summary>
        /// Gets or sets whether or not hidden columns should be included in the text representation
        /// of rows that are copied or dragged to another application. If this is false (the default),
        /// only visible columns will be included.
        /// </summary>
        [Category("ObjectListView"),
        Description("When rows are copied, will column headers be in the text?."),
        DefaultValue(false)]
        public virtual bool IncludeColumnHeadersInCopy
        {
            get { return includeColumnHeadersInCopy; }
            set { includeColumnHeadersInCopy = value; }
        }
        private bool includeColumnHeadersInCopy = false;

        /// <summary>
        /// Return true if a cell edit operation is currently happening
        /// </summary>
        [Browsable(false)]
        public virtual bool IsCellEditing {
            get { return this.cellEditor != null; }
        }

        /// <summary>
        /// Return true if the ObjectListView is being used within the development environment.
        /// </summary>
        [Browsable(false)]
        public virtual bool IsDesignMode {
            get { return this.DesignMode; }
        }

        /// <summary>
        /// Gets whether or not the current list is filtering its contents
        /// </summary>
        [Browsable(false),
        DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        virtual public bool IsFiltering {
            get { return this.UseFiltering && (this.ModelFilter != null || this.ListFilter != null); }
        }

        /// <summary>
        /// When the user types into a list, should the values in the current sort column be searched to find a match?
        /// If this is false, the primary column will always be used regardless of the sort column.
        /// </summary>
        /// <remarks>When this is true, the behavior is like that of ITunes.</remarks>
        [Category("ObjectListView"),
        Description("When the user types into a list, should the values in the current sort column be searched to find a match?"),
        DefaultValue(true)]
        public virtual bool IsSearchOnSortColumn {
            get { return isSearchOnSortColumn; }
            set { isSearchOnSortColumn = value; }
        }
        private bool isSearchOnSortColumn = true;

        /// <summary>
        /// Gets or sets if this control will use a SimpleDropSink to receive drops
        /// </summary>
        /// <remarks>
        /// <para>
        /// Setting this replaces any previous DropSink.
        /// </para>
        /// <para>
        /// After setting this to true, the SimpleDropSink will still need to be configured
        /// to say when it can accept drops and what should happen when something is dropped.
        /// The need to do these things makes this property mostly useless :(
        /// </para>
        /// </remarks>
        [Category("ObjectListView"),
        Description("Should this control will use a SimpleDropSink to receive drops."),
        DefaultValue(false)]
        public virtual bool IsSimpleDropSink {
            get { return this.DropSink != null; }
            set {
                if (value)
                    this.DropSink = new SimpleDropSink();
                else
                    this.DropSink = null;
            }
        }

        /// <summary>
        /// Gets or sets if this control will use a SimpleDragSource to initiate drags
        /// </summary>
        /// <remarks>Setting this replaces any previous DragSource</remarks>
        [Category("ObjectListView"),
        Description("Should this control use a SimpleDragSource to initiate drags out from this control"),
        DefaultValue(false)]
        public virtual bool IsSimpleDragSource {
            get { return this.DragSource != null; }
            set {
                if (value)
                    this.DragSource = new SimpleDragSource();
                else
                    this.DragSource = null;
            }
        }

        /// <summary>
        /// Hide the Items collection so it's not visible in the Properties grid.
        /// </summary>
        [Browsable(false),
         DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        new public ListViewItemCollection Items {
            get { return base.Items; }
        }

        /// <summary>
        /// This renderer draws the items when in the list is in non-details view.
        /// In details view, the renderers for the individuals columns are responsible.
        /// </summary>
        [Category("ObjectListView"),
        Description("The owner drawn renderer that draws items when the list is in non-Details view."),
        DefaultValue(null)]
        public IRenderer ItemRenderer {
            get { return itemRenderer; }
            set { itemRenderer = value; }
        }
        private IRenderer itemRenderer;

        /// <summary>
        /// Which column did we last sort by
        /// </summary>
        /// <remarks>This is an alias for PrimarySortColumn</remarks>
        [Browsable(false),
        DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public virtual OLVColumn LastSortColumn {
            get { return this.PrimarySortColumn; }
            set { this.PrimarySortColumn = value; }
        }

        /// <summary>
        /// Which direction did we last sort
        /// </summary>
        /// <remarks>This is an alias for PrimarySortOrder</remarks>
        [Browsable(false),
        DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public virtual SortOrder LastSortOrder {
            get { return this.PrimarySortOrder; }
            set { this.PrimarySortOrder = value; }
        }

        /// <summary>
        /// Gets or  sets the filter that is applied to our whole list of objects.
        /// </summary>
        /// <remarks>
        /// The list is updated immediately to reflect this filter. 
        /// </remarks>
        [Browsable(false),
        DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public virtual IListFilter ListFilter {
            get { return listFilter; }
            set {
                listFilter = value;
                if (this.UseFiltering)
                    this.UpdateFiltering();
            }
        }
        private IListFilter listFilter;

        /// <summary>
        /// Gets or  sets the filter that is applied to each model objects in the list
        /// </summary>
        /// <remarks>
        /// The list is updated immediately to reflect this filter. 
        /// </remarks>
        [Browsable(false),
        DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public virtual IModelFilter ModelFilter {
            get { return modelFilter; }
            set {
                modelFilter = value;
                if (this.UseFiltering)
                    this.UpdateFiltering();
            }
        }
        private IModelFilter modelFilter;


        /// <summary>
        /// Gets the hit test info last time the mouse was moved.
        /// </summary>
        /// <remarks>Useful for hot item processing.</remarks>
        [Browsable(false),
        DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public virtual OlvListViewHitTestInfo MouseMoveHitTest {
            get { return mouseMoveHitTest; }
            private set { mouseMoveHitTest = value; }
        }
        private OlvListViewHitTestInfo mouseMoveHitTest;
        
        /// <summary>
        /// Gets or sets the list of groups shown by the listview.
        /// </summary>
        /// <remarks>
        /// This property does not work like the .NET Groups property. It should
        /// be treated as a read-only property.
        /// Changes made to the list are NOT reflected in the ListView itself -- it is pointless to add
        /// or remove groups to/from this list. Such modifications will do nothing.
        /// To do such things, you must listen for
        /// BeforeCreatingGroups or AboutToCreateGroups events, and change the list of
        /// groups in those events.
        /// </remarks>
        [Browsable(false),
        DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public IList<OLVGroup> OLVGroups {
            get { return this.olvGroups; }
            set { this.olvGroups = value; }
        }
        private IList<OLVGroup> olvGroups;

        /// <summary>
        /// Gets or sets whether the user wants to owner draw the header control
        /// themselves. If this is false (the default), ObjectListView will use
        /// custom drawing to render the header, if needed.
        /// </summary>
        /// <remarks>
        /// If you listen for the DrawColumnHeader event, you need to set this to true,
        /// otherwise your event handler will not be called.
        /// </remarks>
        [Category("ObjectListView"),
         Description("Should the DrawColumnHeader event be triggered"),
         DefaultValue(false)]
        public bool OwnerDrawnHeader {
            get { return ownerDrawnHeader; }
            set { ownerDrawnHeader = value; }
        }
        private bool ownerDrawnHeader;

        /// <summary>
        /// Get/set the collection of objects that this list will show
        /// </summary>
        /// <remarks>
        /// <para>
        /// The contents of the control will be updated immediately after setting this property.
        /// </para>
        /// <para>This method preserves selection, if possible. Use SetObjects() if
        /// you do not want to preserve the selection. Preserving selection is the slowest part of this
        /// code and performance is O(n) where n is the number of selected rows.</para>
        /// <para>This method is not thread safe.</para>
        /// <para>The property DOES work on virtual lists: setting is problem-free, but if you try to get it
        /// and the list has 10 million objects, it may take some time to return.</para>
        /// </remarks>
        [Browsable(false),
         DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public virtual IEnumerable Objects {
            get {
                return this.objects;
            }
            set {
                this.BeginUpdate();
                try {
                    IList previousSelection = this.SelectedObjects;
                    this.SetObjects(value);
                    this.SelectedObjects = previousSelection;
                } finally {
                    this.EndUpdate();
                }
            }
        }
        private IEnumerable objects;

        /// <summary>
        /// Gets or sets the image that will be drawn over the top of the ListView
        /// </summary>
        [Category("ObjectListView"),
         Description("The image that will be drawn over the top of the ListView"),
         DesignerSerializationVisibility(DesignerSerializationVisibility.Content)]
        public ImageOverlay OverlayImage {
            get { return this.imageOverlay; }
            set {
                if (this.imageOverlay == value)
                    return;

                this.RemoveOverlay(this.imageOverlay);
                this.imageOverlay = value;
                this.AddOverlay(this.imageOverlay);
            }
        }
        private ImageOverlay imageOverlay;

        /// <summary>
        /// Gets or sets the text that will be drawn over the top of the ListView
        /// </summary>
        [Category("ObjectListView"),
         Description("The text that will be drawn over the top of the ListView"),
         DesignerSerializationVisibility(DesignerSerializationVisibility.Content)]
        public TextOverlay OverlayText {
            get { return this.textOverlay; }
            set {
                if (this.textOverlay == value)
                    return;

                this.RemoveOverlay(this.textOverlay);
                this.textOverlay = value;
                this.AddOverlay(this.textOverlay);
            }
        }
        private TextOverlay textOverlay;

        /// <summary>
        /// Gets or sets the transparency of all the overlays.
        /// 0 is completely transparent, 255 is completely opaque.
        /// </summary>
        /// <remarks>
        /// This is obsolete. Use Transparency on each overlay.
        /// </remarks>
        [Browsable(false),
         DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int OverlayTransparency {
            get { return this.overlayTransparency; }
            set { this.overlayTransparency = Math.Min(255, Math.Max(0, value)); }
        }
        private int overlayTransparency = 128;

        /// <summary>
        /// Gets the list of overlays that will be drawn on top of the ListView
        /// </summary>
        /// <remarks>
        /// You can add new overlays and remove overlays that you have added, but
        /// don't mess with the overlays that you didn't create.
        /// </remarks>
        [Browsable(false)]
        protected IList<IOverlay> Overlays {
            get { return this.overlays; }
        }
        private List<IOverlay> overlays = new List<IOverlay>();

        /// <summary>
        /// Which column did we last sort by
        /// </summary>
        [Browsable(false),
        DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public virtual OLVColumn PrimarySortColumn {
            get { return this.primarySortColumn; }
            set {
                this.primarySortColumn = value;
                if (this.TintSortColumn) {
                    this.SelectedColumn = value;
                }
            }
        }
        private OLVColumn primarySortColumn;

        /// <summary>
        /// Which direction did we last sort
        /// </summary>
        [Browsable(false),
        DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public virtual SortOrder PrimarySortOrder {
            get { return primarySortOrder; }
            set { primarySortOrder = value; }
        }
        private SortOrder primarySortOrder;

        /// <summary>
        /// Gets or sets if non-editable checkboxes are drawn as disabled.
        /// </summary>
        /// <remarks>
        /// <para>This only has effect in owner drawn mode.</para>
        /// </remarks>
        [Category("ObjectListView"),
         Description("Should non-editable checkboxes be drawn as disabled?"),
         DefaultValue(false)]
        public virtual bool RenderNonEditableCheckboxesAsDisabled {
            get { return renderNonEditableCheckboxesAsDisabled; }
            set { renderNonEditableCheckboxesAsDisabled = value; }
        }
        private bool renderNonEditableCheckboxesAsDisabled = false;

        /// <summary>
        /// Specify the height of each row in the control in pixels.
        /// </summary>
        /// <remarks><para>The row height in a listview is normally determined by the font size and the small image list size.
        /// This setting allows that calculation to be overridden (within reason: you still cannot set the line height to be
        /// less than the line height of the font used in the control). </para>
        /// <para>Setting it to -1 means use the normal calculation method.</para>
        /// <para><bold>This feature is experiemental!</bold> Strange things may happen to your program,
        /// your spouse or your pet if you use it.</para>
        /// </remarks>
        [Category("ObjectListView"),
         Description("Specify the height of each row in pixels. -1 indicates default height"),
         DefaultValue(-1)]
        public virtual int RowHeight {
            get { return rowHeight; }
            set {
                if (value < 1)
                    rowHeight = -1;
                else
                    rowHeight = value;
                if (this.DesignMode)
                    return;
                this.SetupBaseImageList();
                if (this.CheckBoxes)
                    this.InitializeStateImageList();
            }
        }
        private int rowHeight = -1;

        /// <summary>
        /// How many pixels high is each row?
        /// </summary>
        [Browsable(false)]
        public virtual int RowHeightEffective {
            get {
                switch (this.View) {
                    case View.List:
                    case View.SmallIcon:
                    case View.Details:
                        return Math.Max(this.SmallImageSize.Height, this.Font.Height);

                    case View.Tile:
                        return this.TileSize.Height;

                    case View.LargeIcon:
                        if (this.LargeImageList == null)
                            return this.Font.Height;
                        else
                            return Math.Max(this.LargeImageList.ImageSize.Height, this.Font.Height);

                    default:
                        // This should never happen
                        return 0;
                }
            }
        }

        /// <summary>
        /// How many rows appear on each page of this control
        /// </summary>
        [Browsable(false)]
        public virtual int RowsPerPage {
            get {
                return NativeMethods.GetCountPerPage(this);
            }
        }

        /// <summary>
        /// Get/set the column that will be used to resolve comparisons that are equal when sorting.
        /// </summary>
        /// <remarks>There is no user interface for this setting. It must be set programmatically.</remarks>
        [Browsable(false),
         DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public virtual OLVColumn SecondarySortColumn {
            get { return this.secondarySortColumn; }
            set { this.secondarySortColumn = value; }
        }
        private OLVColumn secondarySortColumn;

        /// <summary>
        /// When the SecondarySortColumn is used, in what order will it compare results?
        /// </summary>
        [Browsable(false),
        DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public virtual SortOrder SecondarySortOrder {
            get { return this.secondarySortOrder; }
            set { this.secondarySortOrder = value; }
        }
        private SortOrder secondarySortOrder = SortOrder.None;

        /// <summary>
        /// Gets or sets if all rows should be selected when the user presses Ctrl-A
        /// </summary>
        [Category("ObjectListView"),
        Description("Should the control select all rows when the user presses Ctrl-A?"),
        DefaultValue(true)]
        public virtual bool SelectAllOnControlA {
            get { return selectAllOnControlA; }
            set { selectAllOnControlA = value; }
        }
        private bool selectAllOnControlA = true;

        /// <summary>
        /// When the user right clicks on the column headers, should a menu be presented which will allow
        /// them to choose which columns will be shown in the view?
        /// </summary>
        /// <remarks>This is just a compatibility wrapper for the SelectColumnsOnRightClickBehaviour
        /// property.</remarks>
        [Category("ObjectListView"),
        Description("When the user right clicks on the column headers, should a menu be presented which will allow them to choose which columns will be shown in the view?"),
        DefaultValue(true)]
        public virtual bool SelectColumnsOnRightClick {
            get { return this.SelectColumnsOnRightClickBehaviour != ColumnSelectBehaviour.None; }
            set {
                if (value) {
                    if (this.SelectColumnsOnRightClickBehaviour == ColumnSelectBehaviour.None)
                        this.SelectColumnsOnRightClickBehaviour = ColumnSelectBehaviour.InlineMenu;
                } else {
                    this.SelectColumnsOnRightClickBehaviour = ColumnSelectBehaviour.None;
                }
            }
        }

        /// <summary>
        /// Gets or sets how the user will be able to select columns when the header is right clicked
        /// </summary>
        [Category("ObjectListView"),
        Description("When the user right clicks on the column headers, how will the user be able to select columns?"),
        DefaultValue(ColumnSelectBehaviour.InlineMenu)]
        public virtual ColumnSelectBehaviour SelectColumnsOnRightClickBehaviour {
            get { return selectColumnsOnRightClickBehaviour; }
            set { selectColumnsOnRightClickBehaviour = value; }
        }
        private ColumnSelectBehaviour selectColumnsOnRightClickBehaviour = ColumnSelectBehaviour.InlineMenu;

        /// <summary>
        /// When the column select menu is open, should it stay open after an item is selected?
        /// Staying open allows the user to turn more than one column on or off at a time.
        /// </summary>
        /// <remarks>This only works when SelectColumnsOnRightClickBehaviour is set to InlineMenu.
        /// It has no effect when the behaviour is set to SubMenu.</remarks>
        [Category("ObjectListView"),
        Description("When the column select inline menu is open, should it stay open after an item is selected?"),
        DefaultValue(true)]
        public virtual bool SelectColumnsMenuStaysOpen {
            get { return selectColumnsMenuStaysOpen; }
            set { selectColumnsMenuStaysOpen = value; }
        }
        private bool selectColumnsMenuStaysOpen = true;

        /// <summary>
        /// Gets or sets the column that is drawn with a slight tint.
        /// </summary>
        /// <remarks>
        /// <para>
        /// If TintSortColumn is true, the sort column will automatically
        /// be made the selected column.
        /// </para>
        /// <para>
        /// The colour of the tint is controlled by SelectedColumnTint.
        /// </para>
        /// </remarks>
        [Browsable(false),
        DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public OLVColumn SelectedColumn {
            get { return this.selectedColumn; }
            set {
                this.selectedColumn = value;
                if (value == null) {
                    this.RemoveDecoration(this.selectedColumnDecoration);
                } else {
                    if (!this.HasDecoration(this.selectedColumnDecoration))
                        this.AddDecoration(this.selectedColumnDecoration);
                }
            }
        }
        private OLVColumn selectedColumn;
        private TintedColumnDecoration selectedColumnDecoration = new TintedColumnDecoration();

        /// <summary>
        /// Gets or sets the decoration that will be drawn on all selected rows
        /// </summary>
        [Browsable(false),
        DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public virtual IDecoration SelectedRowDecoration {
            get { return this.selectedRowDecoration; }
            set { this.selectedRowDecoration = value; }
        }
        private IDecoration selectedRowDecoration;

        /// <summary>
        /// What color should be used to tint the selected column?
        /// </summary>
        /// <remarks>
        /// The tint color must be alpha-blendable, so if the given color is solid
        /// (i.e. alpha = 255), it will be changed to have a reasonable alpha value.
        /// </remarks>
        [Category("ObjectListView"),
         Description("The color that will be used to tint the selected column"),
         DefaultValue(typeof(Color), "")]
        public virtual Color SelectedColumnTint {
            get { return selectedColumnTint; }
            set {
                if (value.A == 255)
                    this.selectedColumnTint = Color.FromArgb(15, value);
                else
                    this.selectedColumnTint = value;
                this.selectedColumnDecoration.Tint = this.selectedColumnTint;
            }
        }
        private Color selectedColumnTint = Color.Empty;

        /// <summary>
        /// Gets or sets the index of the row that is currently selected. 
        /// When getting the index, if no row is selected,or more than one is selected, return -1.
        /// </summary>
        [Browsable(false),
         DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public virtual int SelectedIndex {
            get {
                if (this.SelectedIndices.Count == 1)
                    return this.SelectedIndices[0];
                else
                    return -1;
            }
            set {
                this.SelectedIndices.Clear();
                if (value >= 0 && value < this.Items.Count)
                    this.SelectedIndices.Add(value);
            }
        }

        /// <summary>
        /// Gets or sets the ListViewItem that is currently selected . If no row is selected, or more than one is selected, return null.
        /// </summary>
        [Browsable(false),
         DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public virtual OLVListItem SelectedItem {
            get {
                if (this.SelectedIndices.Count == 1)
                    return this.GetItem(this.SelectedIndices[0]);
                else
                    return null;
            }
            set {
                this.SelectedIndices.Clear();
                if (value != null)
                    this.SelectedIndices.Add(value.Index);
            }
        }

        /// <summary>
        /// Gets the model object from the currently selected row, if there is only one row selected. 
        /// If no row is selected, or more than one is selected, returns null.
        /// When setting, this will select the row that is displaying the given model object and focus on it. 
        /// All other rows are deselected.
        /// </summary>
        [Browsable(false),
         DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public virtual Object SelectedObject {
            get {
                if (this.SelectedIndices.Count == 1)
                    return this.GetModelObject(this.SelectedIndices[0]);
                else
                    return null;
            }
            set {
                // If the given model is already selected, don't do anything else (prevents an flicker)
                object selectedObject = this.SelectedObject;
                if (selectedObject != null && selectedObject.Equals(value))
                    return;

                this.SelectedIndices.Clear();
                this.SelectObject(value, true); 
            }
        }

        /// <summary>
        /// Get the model objects from the currently selected rows. If no row is selected, the returned List will be empty.
        /// When setting this value, select the rows that is displaying the given model objects. All other rows are deselected.
        /// </summary>
        [Browsable(false),
         DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public virtual IList SelectedObjects {
            get {
                ArrayList objects = new ArrayList();
                foreach (int index in this.SelectedIndices)
                  objects.Add(this.GetModelObject(index));
                return objects;
            }
            set {
                this.SelectedIndices.Clear();
                this.SelectObjects(value); 
            }
        }

        /// <summary>
        /// When the user right clicks on the column headers, should a menu be presented which will allow
        /// them to choose common tasks to perform on the listview?
        /// </summary>
        [Category("ObjectListView"),
        Description("When the user right clicks on the column headers, should a menu be presented which will allow them to perform common tasks on the listview?"),
        DefaultValue(false)]
        public virtual bool ShowCommandMenuOnRightClick {
            get { return showCommandMenuOnRightClick; }
            set { showCommandMenuOnRightClick = value; }
        }
        private bool showCommandMenuOnRightClick = false;

        /// <summary>
        /// Gets or sets whether this ObjectListView will show Excel like filtering
        /// menus when the header control is right clicked
        /// </summary>
        [Category("ObjectListView"),
         Description("If this is true, right clicking on a column header will show a Filter menu option"),
         DefaultValue(true)]
        public bool ShowFilterMenuOnRightClick {
            get { return showFilterMenuOnRightClick; }
            set { showFilterMenuOnRightClick = value; }
        }
        private bool showFilterMenuOnRightClick = true;

        /// <summary>
        /// Should this list show its items in groups?
        /// </summary>
        [Category("Appearance"),
         Description("Should the list view show items in groups?"),
         DefaultValue(true)]
        new public virtual bool ShowGroups {
            get { return base.ShowGroups; }
            set {
                this.GroupImageList = this.GroupImageList;
                base.ShowGroups = value; 
            }
        }

        /// <summary>
        /// Should the list view show a bitmap in the column header to show the sort direction?
        /// </summary>
        /// <remarks>
        /// The only reason for not wanting to have sort indicators is that, on pre-XP versions of
        /// Windows, having sort indicators required the ListView to have a small image list, and
        /// as soon as you give a ListView a SmallImageList, the text of column 0 is bumped 16
        /// pixels to the right, even if you never used an image.
        /// </remarks>
        [Category("ObjectListView"),
         Description("Should the list view show sort indicators in the column headers?"),
         DefaultValue(true)]
        public virtual bool ShowSortIndicators {
            get { return showSortIndicators; }
            set { showSortIndicators = value; }
        }
        private bool showSortIndicators;

        /// <summary>
        /// Should the list view show images on subitems?
        /// </summary>
        /// <remarks>
        /// <para>Virtual lists have to be owner drawn in order to show images on subitems</para>
        /// </remarks>
        [Category("ObjectListView"),
         Description("Should the list view show images on subitems?"),
         DefaultValue(false)]
        public virtual bool ShowImagesOnSubItems {
            get { return showImagesOnSubItems; }
            set {
                showImagesOnSubItems = value;
                if (this.Created)
                    this.ApplyExtendedStyles();
                if (value && this.VirtualMode)
                    this.OwnerDraw = true;
            }
        }
        private bool showImagesOnSubItems;

        /// <summary>
        /// This property controls whether group labels will be suffixed with a count of items.
        /// </summary>
        /// <remarks>
        /// The format of the suffix is controlled by GroupWithItemCountFormat/GroupWithItemCountSingularFormat properties
        /// </remarks>
        [Category("ObjectListView"),
         Description("Will group titles be suffixed with a count of the items in the group?"),
         DefaultValue(false)]
        public virtual bool ShowItemCountOnGroups {
            get { return showItemCountOnGroups; }
            set { showItemCountOnGroups = value; }
        }
        private bool showItemCountOnGroups;

        /// <summary>
        /// Gets or sets whether the control will show column headers in all
        /// views (true), or only in Details view (false)
        /// </summary>
        /// <remarks>
        /// <para>
        /// This property is not working correctly. JPP 2010/04/06.
        /// It works fine if it is set before the control is created.
        /// But if it turned off once the control is created, the control
        /// loses its checkboxes (weird!)
        /// </para>
        /// <para>
        /// To changed this setting after the control is created, things
        /// are complicated. If it is off and we want it on, we have
        /// to change the View and the header will appear. If it is currently
        /// on and we want to turn it off, we have to both change the view
        /// AND recreate the handle. Recreating the handle is a problem 
        /// since it makes our checkbox style disappear. 
        /// </para>
        /// </remarks>
        [Category("ObjectListView"),
        Description("Will the control will show column headers in all views?"),
        DefaultValue(true)]
        public bool ShowHeaderInAllViews {
            get { return showHeaderInAllViews; }
            set {
                if (showHeaderInAllViews == value)
                    return;

                showHeaderInAllViews = value;

                // If the control isn't already created, everything is fine.
                if (!this.Created)
                    return;

                // If the header is being hidden, we have to recreate the control
                // to remove the style (not sure why this is)
                if (!showHeaderInAllViews)
                    this.RecreateHandle();

                // Still more complications. The change doesn't become visible until the View is changed
                if (this.View != View.Details) {
                    View temp = this.View;
                    this.View = View.Details;
                    this.View = temp;
                }
            }
        }
        private bool showHeaderInAllViews = true;

        /// <summary>
        /// Override the SmallImageList property so we can correctly shadow its operations.
        /// </summary>
        /// <remarks><para>If you use the RowHeight property to specify the row height, the SmallImageList
        /// must be fully initialised before setting/changing the RowHeight. If you add new images to the image
        /// list after setting the RowHeight, you must assign the imagelist to the control again. Something as simple
        /// as this will work:
        /// <code>listView1.SmallImageList = listView1.SmallImageList;</code></para>
        /// </remarks>
        new public ImageList SmallImageList {
            get { return this.shadowedImageList; }
            set {
                this.shadowedImageList = value;
                if (this.UseSubItemCheckBoxes)
                    this.SetupSubItemCheckBoxes();
                this.SetupBaseImageList();
            }
        }
        private ImageList shadowedImageList;

        /// <summary>
        /// Return the size of the images in the small image list or a reasonable default
        /// </summary>
        [Browsable(false)]
        public virtual Size SmallImageSize {
            get {
                if (this.SmallImageList == null)
                    return new Size(16, 16);
                else
                    return this.SmallImageList.ImageSize;
            }
        }

        /// <summary>
        /// When the listview is grouped, should the items be sorted by the primary column?
        /// If this is false, the items will be sorted by the same column as they are grouped.
        /// </summary>
        [Category("ObjectListView"),
         Description("When the listview is grouped, should the items be sorted by the primary column? If this is false, the items will be sorted by the same column as they are grouped."),
         DefaultValue(true)]
        public virtual bool SortGroupItemsByPrimaryColumn {
            get { return this.sortGroupItemsByPrimaryColumn; }
            set { this.sortGroupItemsByPrimaryColumn = value; }
        }
        private bool sortGroupItemsByPrimaryColumn = true;

        /// <summary>
        /// When the listview is grouped, how many pixels should exist between the end of one group and the
        /// beginning of the next?
        /// </summary>
        [Category("ObjectListView"),
         Description("How many pixels of space will be between groups"),
         DefaultValue(0)]
        public virtual int SpaceBetweenGroups {
            get { return this.spaceBetweenGroups; }
            set { this.spaceBetweenGroups = value; }
        }
        private int spaceBetweenGroups;

        /// <summary>
        /// Should the sort column show a slight tinge?
        /// </summary>
        [Category("ObjectListView"),
         Description("Should the sort column show a slight tinting?"),
         DefaultValue(false)]
        public virtual bool TintSortColumn {
            get { return this.tintSortColumn; }
            set {
                this.tintSortColumn = value;
                if (value && this.LastSortColumn != null)
                    this.SelectedColumn = this.LastSortColumn;
                else
                    this.SelectedColumn = null;
            }
        }
        private bool tintSortColumn;

        /// <summary>
        /// Should each row have a tri-state checkbox?
        /// </summary>
        /// <remarks>
        /// If this is true, the user can choose the third state (normally Indeterminate). Otherwise, user clicks
        /// alternate between checked and unchecked. CheckStateGetter can still return Indeterminate when this
        /// setting is false.
        /// </remarks>
        [Category("ObjectListView"),
         Description("Should the primary column have a checkbox that behaves as a tri-state checkbox?"),
         DefaultValue(false)]
        public virtual bool TriStateCheckBoxes {
            get { return triStateCheckBoxes; }
            set {
                triStateCheckBoxes = value;
                if (value && !this.CheckBoxes)
                    this.CheckBoxes = true;
                this.InitializeStateImageList();
            }
        }
        private bool triStateCheckBoxes;

        /// <summary>
        /// Get or set the index of the top item of this listview
        /// </summary>
        /// <remarks>
        /// <para>
        /// This property only works when the listview is in Details view and not showing groups.
        /// </para>
        /// <para>
        /// The reason that it does not work when showing groups is that, when groups are enabled,
        /// the Windows msg LVM_GETTOPINDEX always returns 0, regardless of the
        /// scroll position.
        /// </para>
        /// </remarks>
        [Browsable(false),
        DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public virtual int TopItemIndex {
            get {
                if (this.View == View.Details && this.TopItem != null)
                    return this.TopItem.Index;
                else
                    return -1;
            }
            set {
                int newTopIndex = Math.Min(value, this.GetItemCount() - 1);
                if (this.View == View.Details && newTopIndex >= 0) {
                    this.TopItem = this.Items[newTopIndex];

                    // Setting the TopItem sometimes gives off by one errors,
                    // that (bizarrely) are correct on a second attempt
                    if (this.TopItem != null && this.TopItem.Index != newTopIndex)
                        this.TopItem = this.GetItem(newTopIndex);
                }
            }
        }

        /// <summary>
        /// When resizing a column by dragging its divider, should any space filling columns be
        /// resized at each mouse move? If this is false, the filling columns will be
        /// updated when the mouse is released.
        /// </summary>
        /// <remarks>
        /// <para>
        /// In previous versions, setting this to true produced ugly behaviour, because every
        /// column to the right of the divider being dragged was updated twice: once when
        /// the column be resized changes size (this moves all the columns slightly to the right);
        /// then again when the filling columns are updated - they are shrunk
        /// so that the combined width is not more than the control, so everything jumps slightly back to the left again.
        /// </para>
        /// <para>
        /// But, as of v2.0, the change the Windows messages in place, so there is now only one update,
        /// and everything looks nice and smooth.
        /// </para>
        /// <para>
        /// However, it still looks odd when the space filling column
        /// is in the left of the column that is being resized: the right edge of the column is dragged, but
        /// its <b>left</b> edge moves, since the space filling column is shrinking.
        /// </para>
        /// <para>Given the above behavior is probably best to turn this property off if your space filling
        /// columns aren't the right-most columns.</para>
        /// </remarks>
        [Category("ObjectListView"),
        Description("When resizing a column by dragging its divider, should any space filling columns be resized at each mouse move?"),
        DefaultValue(true)]
        public virtual bool UpdateSpaceFillingColumnsWhenDraggingColumnDivider {
            get { return updateSpaceFillingColumnsWhenDraggingColumnDivider; }
            set { updateSpaceFillingColumnsWhenDraggingColumnDivider = value; }
        }
        private bool updateSpaceFillingColumnsWhenDraggingColumnDivider = true;

        /// <summary>
        /// What color should be used for the background of selected rows when the control doesn't have the focus?
        /// </summary>
        /// <remarks>Windows does not give the option of changing the selection background.
        /// So the control has to be owner drawn to see the result of this setting.
        /// Setting UseCustomSelectionColors = true will do this for you.</remarks>
        [Category("ObjectListView"),
         Description("The background color of selected rows when the control is owner drawn and doesn't have the focus"),
         DefaultValue(typeof(Color), "")]
        public virtual Color UnfocusedHighlightBackgroundColor {
            get { return unfocusedHighlightBackgroundColor; }
            set { unfocusedHighlightBackgroundColor = value; }
        }
        private Color unfocusedHighlightBackgroundColor = Color.Empty;

        /// <summary>
        /// Return the color should be used for the background of selected rows when the control doesn't have the focus or a reasonable default
        /// </summary>
        [Browsable(false)]
        public virtual Color UnfocusedHighlightBackgroundColorOrDefault {
            get {
                if (this.UnfocusedHighlightBackgroundColor.IsEmpty)
                    return SystemColors.Control;
                else
                    return this.UnfocusedHighlightBackgroundColor;
            }
        }

        /// <summary>
        /// What color should be used for the foreground of selected rows when the control doesn't have the focus?
        /// </summary>
        /// <remarks>Windows does not give the option of changing the selection foreground (text color).
        /// So the control has to be owner drawn to see the result of this setting.
        /// Setting UseCustomSelectionColors = true will do this for you.</remarks>
        [Category("ObjectListView"),
         Description("The foreground color of selected rows when the control is owner drawn and doesn't have the focus"),
         DefaultValue(typeof(Color), "")]
        public virtual Color UnfocusedHighlightForegroundColor {
            get { return unfocusedHighlightForegroundColor; }
            set { unfocusedHighlightForegroundColor = value; }
        }
        private Color unfocusedHighlightForegroundColor = Color.Empty;

        /// <summary>
        /// Return the color should be used for the foreground of selected rows when the control doesn't have the focus or a reasonable default
        /// </summary>
        [Browsable(false)]
        public virtual Color UnfocusedHighlightForegroundColorOrDefault {
            get {
                if (this.UnfocusedHighlightForegroundColor.IsEmpty)
                    return SystemColors.ControlText;
                else
                    return this.UnfocusedHighlightForegroundColor;
            }
        }

        /// <summary>
        /// Should the list give a different background color to every second row?
        /// </summary>
        /// <remarks><para>The color of the alternate rows is given by AlternateRowBackColor.</para>
        /// <para>There is a "feature" in .NET for listviews in non-full-row-select mode, where
        /// selected rows are not drawn with their correct background color.</para></remarks>
        [Category("ObjectListView"),
         Description("Should the list view use a different backcolor to alternate rows?"),
         DefaultValue(false)]
        public virtual bool UseAlternatingBackColors {
            get { return useAlternatingBackColors; }
            set { useAlternatingBackColors = value; }
        }
        private bool useAlternatingBackColors;

        /// <summary>
        /// Should FormatCell events be called for each cell in the control?
        /// </summary>
        /// <remarks>Individual rows can decide whether to call FormatCell
        /// events. This is simply the default behaviour.</remarks>
        [Category("ObjectListView"),
         Description("Should FormatCell events be triggered to every cell that is built?"),
         DefaultValue(false)]
        public bool UseCellFormatEvents {
            get { return useCellFormatEvents; }
            set { useCellFormatEvents = value; }
        }
        private bool useCellFormatEvents;

        /// <summary>
        /// Should the selected row be drawn with non-standard foreground and background colors?
        /// </summary>
        /// <remarks>
        /// When this is enabled, the control becomes owner drawn.
        /// </remarks>
        [Category("ObjectListView"),
         Description("Should the selected row be drawn with non-standard foreground and background colors?"),
         DefaultValue(false)]
        public bool UseCustomSelectionColors {
            get { return this.useCustomSelectionColors; }
            set {
                this.useCustomSelectionColors = value;

                if (!this.DesignMode && value)
                    this.OwnerDraw = true;
            }
        }
        private bool useCustomSelectionColors;

        /// <summary>
        /// Gets or sets whether this ObjectListView will use the same hot item and selection 
        /// mechanism that Vista Explorer does.
        /// </summary>
        /// <remarks>This property has many imperfections:
        /// <list type="bullet">
        /// <item><description>This only works on Vista and later</description></item>
        /// <item><description>It does nothing for owner drawn lists.
        /// Owner drawn lists are (naturally) controlled by their renderers.</description></item>
        /// <item><description>It does not work well with AlternateRowBackColors.</description></item>
        /// <item><description>It does not play well with HotItemStyles.</description></item>
        /// <item><description>It looks a little bit silly is FullRowSelect is false.</description></item>
        /// </list>
        /// But if you absolutely have to look like Vista, this is your property. 
        /// Do not complain if settings this messes up other things.
        /// </remarks>
        [Category("ObjectListView"),
         Description("Should the list use the same hot item and selection mechanism as Vista?"),
         DefaultValue(false)]
        public bool UseExplorerTheme {
            get { return useExplorerTheme; }
            set {
                useExplorerTheme = value;
                if (this.Created)
                    NativeMethods.SetWindowTheme(this.Handle, value ? "explorer" : "", null);
            }
        }
        private bool useExplorerTheme;

        /// <summary>
        /// Gets or sets whether the list should enable filtering
        /// </summary>
        [Category("ObjectListView"),
        Description("Should the list enable filtering?"),
        DefaultValue(false)]
        virtual public bool UseFiltering {
            get { return useFiltering; }
            set {
                useFiltering = value;
                this.UpdateFiltering();
            }
        }
        private bool useFiltering;

        /// <summary>
        /// Gets or sets whether the list should put an indicator into a column's header to show that
        /// it is filtering on that column
        /// </summary>
        [Category("ObjectListView"),
        Description("Should an image be drawn in a column's header when that column is being used for filtering?"),
        DefaultValue(true)]
        virtual public bool UseFilterIndicator {
            get { return useFilterIndicator; }
            set { 
                useFilterIndicator = value;
                this.Invalidate();
            }
        }
        private bool useFilterIndicator = true;

        /// <summary>
        /// Should the item under the cursor be formatted in a special way?
        /// </summary>
        [Category("ObjectListView"),
         Description("Should HotTracking be used? Hot tracking applies special formatting to the row under the cursor"),
         DefaultValue(false)]
        public bool UseHotItem {
            get { return this.useHotItem; }
            set {
                this.useHotItem = value;
                if (this.HotItemStyle != null) {
                    if (value)
                        this.AddOverlay(this.HotItemStyle.Overlay);
                    else
                        this.RemoveOverlay(this.HotItemStyle.Overlay);
                }
            }
        }
        private bool useHotItem;

        /// <summary>
        /// Gets or sets whether this listview should show hyperlinks in the cells.
        /// </summary>
        [Category("ObjectListView"),
         Description("Should hyperlinks be shown on this control?"),
         DefaultValue(false)]
        public bool UseHyperlinks {
            get { return this.useHyperlinks; }
            set {
                this.useHyperlinks = value;
                if (value && this.HyperlinkStyle == null)
                    this.HyperlinkStyle = new HyperlinkStyle();
            }
        }
        private bool useHyperlinks;

        /// <summary>
        /// Should this control show overlays
        /// </summary>
        /// <remarks>Overlays are enabled by default and would only need to be disabled
        /// if they were causing problems in your development environment.</remarks>
        [Category("ObjectListView"),
         Description("Should this control show overlays"),
         DefaultValue(true)]
        public bool UseOverlays {
            get { return this.useOverlays; }
            set { this.useOverlays = value; }
        }
        private bool useOverlays = true;

        /// <summary>
        /// Should this control be configured to show check boxes on subitems?
        /// </summary>
        /// <remarks>If this is set to True, the control will be given a SmallImageList if it
        /// doesn't already have one. Also, if it is a virtual list, it will be set to owner
        /// drawn, since virtual lists can't draw check boxes without being owner drawn.</remarks>
        [Category("ObjectListView"),
         Description("Should this control be configured to show check boxes on subitems."),
         DefaultValue(false)]
        public bool UseSubItemCheckBoxes {
            get { return this.useSubItemCheckBoxes; }
            set {
                this.useSubItemCheckBoxes = value;
                if (value)
                    this.SetupSubItemCheckBoxes();
            }
        }
        private bool useSubItemCheckBoxes;

        /// <summary>
        /// Gets or sets if the ObjectListView will use a translucent selection mechanism like Vista.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Unlike UseExplorerTheme, this Vista-like scheme works on XP and for both
        /// owner and non-owner drawn lists.
        /// </para>
        /// <para>
        /// This will replace any SelectedRowDecoration that has been installed.
        /// </para>
        /// </remarks>
        [Category("ObjectListView"),
         Description("Should the list use a translucent selection mechanism (like Vista)"),
         DefaultValue(false)]
        public bool UseTranslucentSelection {
            get { return useTranslucentSelection; }
            set {
                useTranslucentSelection = value;
                if (value) {
                    RowBorderDecoration rbd = new RowBorderDecoration();
                    rbd.BorderPen = new Pen(Color.FromArgb(154, 223, 251));
                    rbd.FillBrush = new SolidBrush(Color.FromArgb(48, 163, 217, 225));
                    rbd.BoundsPadding = new Size(0, 0);
                    rbd.CornerRounding = 6.0f;
                    this.SelectedRowDecoration = rbd;
                } else
                    this.SelectedRowDecoration = null;
            }
        }
        private bool useTranslucentSelection;
        
        /// <summary>
        /// Gets or sets if the ObjectListView will use a translucent hot row highlighting mechanism like Vista.
        /// </summary>
        /// <remarks>Setting this will replace any HotItemStyle that has been </remarks>
        [Category("ObjectListView"),
         Description("Should the list use a translucent hot row highlighting mechanism (like Vista)"),
         DefaultValue(false)]
        public bool UseTranslucentHotItem {
            get { return useTranslucentHotItem; }
            set {
                useTranslucentHotItem = value;
                if (value) {
                    this.HotItemStyle = new HotItemStyle();
                    RowBorderDecoration rbd = new RowBorderDecoration();
                    rbd.BorderPen = new Pen(Color.FromArgb(154, 223, 251));
                    rbd.BoundsPadding = new Size(0, 0);
                    rbd.CornerRounding = 6.0f;
                    rbd.FillGradientFrom = Color.FromArgb(0, 255, 255, 255);
                    rbd.FillGradientTo = Color.FromArgb(64, 183, 237, 240);
                    this.HotItemStyle.Decoration = rbd;
                } else
                    this.HotItemStyle = null;
                this.UseHotItem = value;
            }
        }
        private bool useTranslucentHotItem;

        /// <summary>
        /// Get/set the style of view that this listview is using
        /// </summary>
        /// <remarks>Switching to tile or details view installs the columns appropriate to that view.
        /// Confusingly, in tile view, every column is shown as a row of information.</remarks>
        new public View View {
            get { return base.View; }
            set {
                if (base.View == value)
                    return;

                if (this.Frozen) {
                    base.View = value;
                    this.SetupBaseImageList();
                } else {
                    this.Freeze();

                    if (value == View.Tile)
                        this.CalculateReasonableTileSize();

                    base.View = value;
                    this.SetupBaseImageList();
                    this.Unfreeze();
                }
            }
        }

        #endregion

        #region Callbacks

        /// <summary>
        /// This delegate fetches the checkedness of an object as a boolean only.
        /// </summary>
        /// <remarks>Use this if you never want to worry about the
        /// Indeterminate state (which is fairly common).
        /// <para>
        /// This is a convenience wrapper around the CheckStateGetter property.
        /// </para>
        /// </remarks>
        [Browsable(false),
        DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public virtual BooleanCheckStateGetterDelegate BooleanCheckStateGetter {
            set {
                if (value == null)
                    this.CheckStateGetter = null;
                else
                    this.CheckStateGetter = delegate(Object x) {
                        return value(x) ? CheckState.Checked : CheckState.Unchecked;
                    };
            }
        }

        /// <summary>
        /// This delegate sets the checkedness of an object as a boolean only. It must return
        /// true or false indicating if the object was checked or not.
        /// </summary>
        /// <remarks>Use this if you never want to worry about the
        /// Indeterminate state (which is fairly common).
        /// <para>
        /// This is a convenience wrapper around the CheckStatePutter property.
        /// </para>
        /// </remarks>
        [Browsable(false),
         DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public virtual BooleanCheckStatePutterDelegate BooleanCheckStatePutter {
            set {
                if (value == null)
                    this.CheckStatePutter = null;
                else
                    this.CheckStatePutter = delegate(Object x, CheckState state) {
                        bool isChecked = (state == CheckState.Checked);
                        return value(x, isChecked) ? CheckState.Checked : CheckState.Unchecked;
                    };
            }
        }

        /// <summary>
        /// Gets whether or not this listview is capabale of showing groups
        /// </summary>
        [Browsable(false)]
        public virtual bool CanShowGroups {
            get {
                return true;
            }
        }

        /// <summary>
        /// This delegate is called when the list wants to show a tooltip for a particular cell.
        /// The delegate should return the text to display, or null to use the default behavior
        /// (which is to show the full text of truncated cell values).
        /// </summary>
        /// <remarks>
        /// Displaying the full text of truncated cell values only work for FullRowSelect listviews.
        /// This is MS's behavior, not mine. Don't complain to me :)
        /// </remarks>
        [Browsable(false),
         DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public virtual CellToolTipGetterDelegate CellToolTipGetter {
            get { return cellToolTipGetter; }
            set { cellToolTipGetter = value; }
        }
        private CellToolTipGetterDelegate cellToolTipGetter;

        /// <summary>
        /// The name of the property (or field) that holds whether or not a model is checked.
        /// </summary>
        /// <remarks>
        /// <para>The property be modifiable. It must have a return type of bool (or of bool? if
        /// TriStateCheckBoxes is true).</para>
        /// <para>Setting this property replaces any CheckStateGetter or CheckStatePutter that have been installed.
        /// Conversely, later setting the CheckStateGetter or CheckStatePutter properties will take precedence
        /// over the behavior of this property.</para>
        /// </remarks>
        [Category("ObjectListView"),
         Description("The name of the property or field that holds the 'checkedness' of the model"),
         DefaultValue(null)]
        public virtual string CheckedAspectName {
            get { return checkedAspectName; }
            set {
                checkedAspectName = value;
                if (String.IsNullOrEmpty(checkedAspectName)) {
                    this.checkedAspectMunger = null;
                    this.CheckStateGetter = null;
                    this.CheckStatePutter = null;
                } else {
                    this.checkedAspectMunger = new Munger(checkedAspectName);
                    this.CheckStateGetter = delegate(Object modelObject) {
                        bool? result = this.checkedAspectMunger.GetValue(modelObject) as bool?;
                        if (result.HasValue)
                            if (result.Value)
                                return CheckState.Checked;
                            else
                                return CheckState.Unchecked;
                        else
                            if (this.TriStateCheckBoxes)
                                return CheckState.Indeterminate;
                            else
                                return CheckState.Unchecked;
                    };
                    this.CheckStatePutter = delegate(Object modelObject, CheckState newValue) {
                        if (this.TriStateCheckBoxes && newValue == CheckState.Indeterminate)
                            this.checkedAspectMunger.PutValue(modelObject, null);
                        else
                            this.checkedAspectMunger.PutValue(modelObject, newValue == CheckState.Checked);
                        return this.CheckStateGetter(modelObject);
                    };
                }
            }
        }
        private string checkedAspectName;
        private Munger checkedAspectMunger;

        /// <summary>
        /// This delegate will be called whenever the ObjectListView needs to know the check state
        /// of the row associated with a given model object.
        /// </summary>
        /// <remarks>
        /// <para>.NET has no support for indeterminate values, but as of v2.0, this class allows
        /// indeterminate values.</para>
        /// </remarks>
        [Browsable(false),
         DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public virtual CheckStateGetterDelegate CheckStateGetter {
            get { return checkStateGetter; }
            set { checkStateGetter = value; }
        }
        private CheckStateGetterDelegate checkStateGetter;

        /// <summary>
        /// This delegate will be called whenever the user tries to change the check state of a row.
        /// The delegate should return the state that was actually set, which may be different
        /// to the state given.
        /// </summary>
        [Browsable(false),
         DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public virtual CheckStatePutterDelegate CheckStatePutter {
            get { return checkStatePutter; }
            set { checkStatePutter = value; }
        }
        private CheckStatePutterDelegate checkStatePutter;

        /// <summary>
        /// This delegate can be used to sort the table in a custom fasion.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The delegate must install a ListViewItemSorter on the ObjectListView.
        /// Installing the ItemSorter does the actual work of sorting the ListViewItems.
        /// See ColumnComparer in the code for an example of what an ItemSorter has to do.
        /// </para>
        /// <para>
        /// Do not install a CustomSorter on a VirtualObjectListView. Override the SortObjects()
        /// method of the IVirtualListDataSource instead.
        /// </para>
        /// </remarks>
        [Browsable(false),
         DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public virtual SortDelegate CustomSorter {
            get { return customSorter; }
            set { customSorter = value; }
        }
        private SortDelegate customSorter;

        /// <summary>
        /// This delegate is called when the list wants to show a tooltip for a particular header.
        /// The delegate should return the text to display, or null to use the default behavior
        /// (which is to not show any tooltip).
        /// </summary>
        /// <remarks>
        /// Installing a HeaderToolTipGetter takes precedence over any text in OLVColumn.ToolTipText.
        /// </remarks>
        [Browsable(false),
         DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public virtual HeaderToolTipGetterDelegate HeaderToolTipGetter {
            get { return headerToolTipGetter; }
            set { headerToolTipGetter = value; }
        }
        private HeaderToolTipGetterDelegate headerToolTipGetter;

        /// <summary>
        /// This delegate can be used to format a OLVListItem before it is added to the control.
        /// </summary>
        /// <remarks>
        /// <para>The model object for the row can be found through the RowObject property of the OLVListItem object.</para>
        /// <para>All subitems normally have the same style as list item, so setting the forecolor on one
        /// subitem changes the forecolor of all subitems.
        /// To allow subitems to have different attributes, do this:
        /// <code>myListViewItem.UseItemStyleForSubItems = false;</code>.
        /// </para>
        /// <para>If UseAlternatingBackColors is true, the backcolor of the listitem will be calculated
        /// by the control and cannot be controlled by the RowFormatter delegate.
        /// In general, trying to use a RowFormatter
        /// when UseAlternatingBackColors is true does not work well.</para>
        /// <para>As it says in the summary, this is called <b>before</b> the item is added to the control.
        /// Many properties of the OLVListItem itself are not available at that point, including:
        /// Index, Selected, Focused, Bounds, Checked, DisplayIndex.</para>
        /// </remarks>
        [Browsable(false),
         DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public virtual RowFormatterDelegate RowFormatter {
            get { return rowFormatter; }
            set { rowFormatter = value; }
        }
        private RowFormatterDelegate rowFormatter;

        #endregion

        #region List commands

        /// <summary>
        /// Add the given model object to this control.
        /// </summary>
        /// <param name="modelObject">The model object to be displayed</param>
        /// <remarks>See AddObjects() for more details</remarks>
        public virtual void AddObject(object modelObject) {
            if (this.InvokeRequired)
                this.Invoke((MethodInvoker)delegate() { this.AddObject(modelObject); });
            else
                this.AddObjects(new object[] { modelObject });
        }

        /// <summary>
        /// Add the given collection of model objects to this control.
        /// </summary>
        /// <param name="modelObjects">A collection of model objects</param>
        /// <remarks>
        /// <para>The added objects will appear in their correct sort position, if sorting
        /// is active (i.e. if LastSortColumn is not null). Otherwise, they will appear at the end of the list.</para>
        /// <para>No check is performed to see if any of the objects are already in the ListView.</para>
        /// <para>Null objects are silently ignored.</para>
        /// </remarks>
        public virtual void AddObjects(ICollection modelObjects) {
            if (this.InvokeRequired) {
                this.Invoke((MethodInvoker)delegate() { this.AddObjects(modelObjects); });
                return;
            }
            this.InsertObjects(this.GetItemCount(), modelObjects);
            this.Sort(this.LastSortColumn, this.LastSortOrder);

        }

        /// <summary>
        /// Resize the columns to the maximum of the header width and the data.
        /// </summary>		
        public void AutoResizeColumns()
        {
            foreach (OLVColumn c in this.Columns)
            {
                c.Width = -2;
            }
        }

        /// <summary>
        /// Organise the view items into groups, based on the last sort column or the first column
        /// if there is no last sort column
        /// </summary>
        public virtual void BuildGroups() {
            this.BuildGroups(this.LastSortColumn, this.LastSortOrder == SortOrder.None ? SortOrder.Ascending : this.LastSortOrder);
        }

        /// <summary>
        /// Organise the view items into groups, based on the given column
        /// </summary>
        /// <remarks>
        /// <para>
        /// If the AlwaysGroupByColumn property is not null,
        /// the list view items will be organisd by that column,
        /// and the 'column' parameter will be ignored.
        /// </para>
        /// <para>This method triggers sorting events: BeforeSorting and AfterSorting.</para>
        /// </remarks>
        /// <param name="column">The column whose values should be used for sorting.</param>
        /// <param name="order"></param>
        public virtual void BuildGroups(OLVColumn column, SortOrder order) {
            BeforeSortingEventArgs args = this.BuildBeforeSortingEventArgs(column, order);
            this.OnBeforeSorting(args);
            if (args.Canceled)
                return;

            this.BuildGroups(args.ColumnToGroupBy, args.GroupByOrder,
                args.ColumnToSort, args.SortOrder, args.SecondaryColumnToSort, args.SecondarySortOrder);

            this.OnAfterSorting(new AfterSortingEventArgs(args));
        }

        private BeforeSortingEventArgs BuildBeforeSortingEventArgs(OLVColumn column, SortOrder order) {
            OLVColumn groupBy = this.AlwaysGroupByColumn ?? column ?? this.GetColumn(0);
            SortOrder groupByOrder = this.AlwaysGroupBySortOrder;
            if (order == SortOrder.None) {
                order = this.Sorting;
                if (order == SortOrder.None)
                    order = SortOrder.Ascending;
            }
            if (groupByOrder == SortOrder.None)
                groupByOrder = order;

            BeforeSortingEventArgs args = new BeforeSortingEventArgs(
                groupBy, groupByOrder,
                column, order,
                this.SecondarySortColumn ?? this.GetColumn(0),
                this.SecondarySortOrder == SortOrder.None ? order : this.SecondarySortOrder);
            if (column != null)
                args.Canceled = !column.Sortable;
            return args;
        }

        /// <summary>
        /// Organise the view items into groups, based on the given columns
        /// </summary>
        /// <param name="groupByColumn">What column will be used for grouping</param>
        /// <param name="groupByOrder">What ordering will be used for groups</param>
        /// <param name="column">The column whose values should be used for sorting. Cannot be null</param>
        /// <param name="order">The order in which the values from column will be sorted</param>
        /// <param name="secondaryColumn">When the values from 'column' are equal, use the values provided by this column</param>
        /// <param name="secondaryOrder">How will the secondary values be sorted</param>
        /// <remarks>This method does not trigger sorting events. Use BuildGroups() to do that</remarks>
        public virtual void BuildGroups(OLVColumn groupByColumn, SortOrder groupByOrder,
            OLVColumn column, SortOrder order, OLVColumn secondaryColumn, SortOrder secondaryOrder) {
            // Sanity checks
            if (groupByColumn == null)
                return;

            // Getting the Count forces any internal cache of the ListView to be flushed. Without
            // this, iterating over the Items will not work correctly if the ListView handle
            // has not yet been created.
            int dummy = this.Items.Count;

            // Collect all the information that governs the creation of groups
            GroupingParameters parms = this.CollectGroupingParameters(groupByColumn, groupByOrder,
                column, order, secondaryColumn, secondaryOrder);

            // Trigger an event to let the world create groups if they want
            CreateGroupsEventArgs args = new CreateGroupsEventArgs(parms);
            if (parms.GroupByColumn != null)
                args.Canceled = !parms.GroupByColumn.Groupable;
            this.OnBeforeCreatingGroups(args);
            if (args.Canceled)
                return;

            // If the event didn't create them for us, use our default strategy
            if (args.Groups == null) 
                args.Groups = this.MakeGroups(parms);

            // Give the world a chance to munge the groups before they are created
            this.OnAboutToCreateGroups(args);
            if (args.Canceled)
                return;

            // Create the groups now
            this.OLVGroups = args.Groups;
            this.CreateGroups(args.Groups);

            // Tell the world that new groups have been created
            this.OnAfterCreatingGroups(args);
        }

        /// <summary>
        /// Collect and return all the variables that influence the creation of groups
        /// </summary>
        /// <returns></returns>
        protected virtual GroupingParameters CollectGroupingParameters(OLVColumn groupByColumn, SortOrder groupByOrder,
            OLVColumn column, SortOrder order, OLVColumn secondaryColumn, SortOrder secondaryOrder) {
            string titleFormat = this.ShowItemCountOnGroups ? groupByColumn.GroupWithItemCountFormatOrDefault : null;
            string titleSingularFormat = this.ShowItemCountOnGroups ? groupByColumn.GroupWithItemCountSingularFormatOrDefault : null;
            GroupingParameters parms = new GroupingParameters(this, groupByColumn, groupByOrder,
                column, order, secondaryColumn, secondaryOrder,
                titleFormat, titleSingularFormat, this.SortGroupItemsByPrimaryColumn);
            return parms;
        }

        /// <summary>
        /// Make a list of groups that should be shown according to the given parameters
        /// </summary>
        /// <param name="parms"></param>
        /// <returns>The list of groups to be created</returns>
        /// <remarks>This should not change the state of the control. It is possible that the
        /// groups created will not be used. They may simply be discarded.</remarks>
        protected virtual IList<OLVGroup> MakeGroups(GroupingParameters parms) {

            // There is a lot of overlap between this method and FastListGroupingStrategy.MakeGroups()
            // Any changes made here may need to be reflected there

            // Separate the list view items into groups, using the group key as the descrimanent
            NullableDictionary<object, List<OLVListItem>> map = new NullableDictionary<object, List<OLVListItem>>();
            foreach (OLVListItem olvi in parms.ListView.Items) {
                object key = parms.GroupByColumn.GetGroupKey(olvi.RowObject);
                if (!map.ContainsKey(key))
                    map[key] = new List<OLVListItem>();
                map[key].Add(olvi);
            }

            // Sort the items within each group (unless specifically turned off)
            OLVColumn primarySortColumn = parms.SortItemsByPrimaryColumn ? parms.ListView.GetColumn(0) : parms.PrimarySort;
            if (primarySortColumn != null && parms.PrimarySortOrder != SortOrder.None) {
                ColumnComparer itemSorter = new ColumnComparer(primarySortColumn, parms.PrimarySortOrder,
                    parms.SecondarySort, parms.SecondarySortOrder);
                foreach (object key in map.Keys) {
                    map[key].Sort(parms.ItemComparer ?? itemSorter);
                }
            }

            // Make a list of the required groups
            List<OLVGroup> groups = new List<OLVGroup>();
            foreach (object key in map.Keys) {
                string title = parms.GroupByColumn.ConvertGroupKeyToTitle(key);
                if (!String.IsNullOrEmpty(parms.TitleFormat)) {
                    int count = map[key].Count;
                    string format = (count == 1 ? parms.TitleSingularFormat : parms.TitleFormat);
                    try {
                        title = String.Format(format, title, count);
                    } catch (FormatException) {
                        title = "Invalid group format: " + format;
                    }
                }

                OLVGroup lvg = new OLVGroup(title);
                lvg.Collapsible = this.HasCollapsibleGroups;
                lvg.Key = key;
                lvg.SortValue = key as IComparable;
                lvg.Items = map[key];
                if (parms.GroupByColumn.GroupFormatter != null)
                    parms.GroupByColumn.GroupFormatter(lvg, parms);
                groups.Add(lvg);
            }

            // Sort the groups
            if (parms.GroupByOrder != SortOrder.None)
                groups.Sort(parms.GroupComparer ?? new OLVGroupComparer(parms.GroupByOrder));

            return groups;
        }

        /// <summary>
        /// Build/rebuild all the list view items in the list
        /// </summary>
        public virtual void BuildList() {
            if (this.InvokeRequired)
                this.Invoke(new MethodInvoker(this.BuildList));
            else
                this.BuildList(true);
        }

        /// <summary>
        /// Build/rebuild all the list view items in the list
        /// </summary>
        /// <param name="shouldPreserveState">If this is true, the control will try to preserve the selection,
        /// focused item, and the scroll position (see Remarks)
        /// </param>
        /// <remarks>
        /// <para>
        /// Use this method in situations were the contents of the list is basically the same
        /// as previously.
        /// </para>
        /// <para>
        /// Due to limitations in .NET's ListView, the scroll position is only preserved if
        /// the control is in Details view AND it is not showing groups.
        /// </para>
        /// </remarks>
        public virtual void BuildList(bool shouldPreserveState) {
            if (this.Frozen)
                return;

            this.ApplyExtendedStyles();
            this.ClearHotItem();
            int previousTopIndex = this.TopItemIndex;
            Point currentScrollPosition = this.LowLevelScrollPosition;

            IList previousSelection = new ArrayList();
            Object previousFocus = null;
            if (shouldPreserveState && this.objects != null) {
                previousSelection = this.SelectedObjects;
                OLVListItem focusedItem = this.FocusedItem as OLVListItem;
                if (focusedItem != null)
                    previousFocus = focusedItem.RowObject;
            }

            IEnumerable objectsToDisplay = this.FilteredObjects;

            this.BeginUpdate();
            try {
                this.Items.Clear();
                this.ListViewItemSorter = null;

                if (objectsToDisplay != null) {
                    // Build a list of all our items and then display them. (Building
                    // a list and then doing one AddRange is about 10-15% faster than individual adds)
                    List<OLVListItem> itemList = new List<OLVListItem>();
                    foreach (object rowObject in objectsToDisplay) {
                        OLVListItem lvi = new OLVListItem(rowObject);
                        this.FillInValues(lvi, rowObject);
                        itemList.Add(lvi);
                    }
                    this.Items.AddRange(itemList.ToArray());
                    this.Sort();

                    if (shouldPreserveState) {
                        this.SelectedObjects = previousSelection;
                        this.FocusedItem = this.ModelToItem(previousFocus);
                    }

                    this.RefreshHotItem();
                }
            } finally {
                this.EndUpdate();
            }

            // We can only restore the scroll position after the EndUpdate() because
            // of caching that the ListView does internally during a BeginUpdate/EndUpdate pair.
            if (shouldPreserveState) {
                this.RefreshHotItem();

                // Restore the scroll position. TopItemIndex is best, but doesn't work
                // when the control is grouped.
                if (this.ShowGroups)
                    this.LowLevelScroll(currentScrollPosition.X, currentScrollPosition.Y);
                else
                    this.TopItemIndex = previousTopIndex;

            }
        }

        /// <summary>
        /// Apply all required extended styles to our control.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Whenever .NET code sets an extended style, it erases all other extended styles
        /// that it doesn't use. So, we have to explicit reapply the styles that we have
        /// added.
        /// </para>
        /// <para>
        /// Normally, we would override CreateParms property and update
        /// the ExStyle member, but ListView seems to ignore all ExStyles that
        /// it doesn't already know about. Worse, when we set the LVS_EX_HEADERINALLVIEWS 
        /// value, bad things happen (the control crashes!).
        /// </para>
        /// </remarks>
        protected virtual void ApplyExtendedStyles() {
            const int LVS_EX_SUBITEMIMAGES = 0x00000002;
            //const int LVS_EX_TRANSPARENTBKGND = 0x00400000;
            const int LVS_EX_HEADERINALLVIEWS = 0x02000000;

            const int styleMask = LVS_EX_SUBITEMIMAGES | LVS_EX_HEADERINALLVIEWS;
            int style = 0;

            if (this.ShowImagesOnSubItems && !this.VirtualMode)
                style ^= LVS_EX_SUBITEMIMAGES;

            if (this.ShowHeaderInAllViews)
                style ^= LVS_EX_HEADERINALLVIEWS;

            NativeMethods.SetExtendedStyle(this, style, styleMask);
        }

        /// <summary>
        /// Give the listview a reasonable size of its tiles, based on the number of lines of
        /// information that each tile is going to display.
        /// </summary>
        public virtual void CalculateReasonableTileSize() {
            if (this.Columns.Count <= 0)
                return;

            List<OLVColumn> columns = this.AllColumns.FindAll(delegate(OLVColumn x) {
                return (x.Index == 0) || x.IsTileViewColumn;
            });

            int imageHeight = (this.LargeImageList == null ? 16 : this.LargeImageList.ImageSize.Height);
            int dataHeight = (this.Font.Height + 1) * columns.Count;
            int tileWidth = (this.TileSize.Width == 0 ? 200 : this.TileSize.Width);
            int tileHeight = Math.Max(this.TileSize.Height, Math.Max(imageHeight, dataHeight));
            this.TileSize = new Size(tileWidth, tileHeight);
        }

        /// <summary>
        /// Rebuild this list for the given view
        /// </summary>
        /// <param name="view"></param>
        public virtual void ChangeToFilteredColumns(View view) {
            // Store the state
            IList previousSelection = this.SelectedObjects;
            int previousTopIndex = this.TopItemIndex;

            this.Freeze();
            this.Clear();
            List<OLVColumn> columns = this.GetFilteredColumns(view);
            if (view == View.Details) {
                // Make sure all columns have a reasonable LastDisplayIndex
                for (int index = 0; index < columns.Count; index++)
                {
                    if (columns[index].LastDisplayIndex == -1)
                        columns[index].LastDisplayIndex = index;
                }
                // ListView will ignore DisplayIndex FOR ALL COLUMNS if there are any errors, 
                // e.g. duplicates (two columns with the same DisplayIndex) or gaps. 
                // LastDisplayIndex isn't guaranteed to be unique, so we just sort the columns by
                // the last position they were displayed and use that to generate a sequence 
                // we can use for the DisplayIndex values.
                List<OLVColumn> columnsInDisplayOrder = new List<OLVColumn>(columns);
                columnsInDisplayOrder.Sort(delegate(OLVColumn x, OLVColumn y) { return (x.LastDisplayIndex - y.LastDisplayIndex); });
                int i = 0;
                foreach (OLVColumn col in columnsInDisplayOrder)
                    col.DisplayIndex = i++;
            }

            this.Columns.AddRange(columns.ToArray());
            if (view == View.Details)
                this.ShowSortIndicator();
            this.BuildList();
            this.Unfreeze();

            // Restore the state
            this.SelectedObjects = previousSelection;
            this.TopItemIndex = previousTopIndex;
        }

        /// <summary>
        /// Remove all items from this list
        /// </summary>
        /// <remark>This method can safely be called from background threads.</remark>
        public virtual void ClearObjects() {
            if (this.InvokeRequired)
                this.Invoke(new MethodInvoker(this.ClearObjects));
            else
                this.SetObjects(null);
        }

        /// <summary>
        /// Reset the memory of which URLs have been visited
        /// </summary>
        public virtual void ClearUrlVisited() {
            this.visitedUrlMap = new Dictionary<string, bool>();
        }

        /// <summary>
        /// Copy a text and html representation of the selected rows onto the clipboard.
        /// </summary>
        /// <remarks>Be careful when using this with virtual lists. If the user has selected
        /// 10,000,000 rows, this method will faithfully try to copy all of them to the clipboard.
        /// From the user's point of view, your program will appear to have hung.</remarks>
        public virtual void CopySelectionToClipboard() {
            IList selection = this.SelectedObjects;
            if (selection.Count == 0)
                return;

            // Use the DragSource object to create the data object, if so configured.
            // This relies on the assumption that DragSource will handle the selected objects only.
            // It is legal for StartDrag to return null.
            object data = null;
            if (this.CopySelectionOnControlCUsesDragSource && this.DragSource != null)
                data = this.DragSource.StartDrag(this, MouseButtons.Left, this.ModelToItem(selection[0]));

            Clipboard.SetDataObject(data ?? new OLVDataObject(this, selection));
        }

        /// <summary>
        /// Copy a text and html representation of the given objects onto the clipboard.
        /// </summary>
        public virtual void CopyObjectsToClipboard(IList objectsToCopy) {
            if (objectsToCopy.Count == 0)
                return;

            // We don't know where these objects came from, so we can't use the DragSource to create
            // the data object, like we do with CopySelectionToClipboard() above.
            OLVDataObject dataObject = new OLVDataObject(this, objectsToCopy);
            dataObject.CreateTextFormats();
            Clipboard.SetDataObject(dataObject);
        }

        /// <summary>
        /// Return a html representation of the given objects
        /// </summary>
        public virtual string ObjectsToHtml(IList objectsToConvert) {
            if (objectsToConvert.Count == 0)
                return String.Empty;

            OLVDataObject dataObject = new OLVDataObject(this, objectsToConvert);
            return dataObject.CreateHtml();
        }

        /// <summary>
        /// Deselect all rows in the listview
        /// </summary>
        public virtual void DeselectAll() {
            NativeMethods.DeselectAllItems(this);
        }

        /// <summary>
        /// Setup the list so it will draw selected rows using custom colours.
        /// </summary>
        /// <remarks>
        /// This method makes the list owner drawn, and ensures that all columns have at
        /// least a BaseRender installed.
        /// </remarks>
        public virtual void EnableCustomSelectionColors() {
            this.UseCustomSelectionColors = true;
        }

        /// <summary>
        /// Return the ListViewItem that appears immediately after the given item.
        /// If the given item is null, the first item in the list will be returned.
        /// Return null if the given item is the last item.
        /// </summary>
        /// <param name="itemToFind">The item that is before the item that is returned, or null</param>
        /// <returns>A ListViewItem</returns>
        public virtual OLVListItem GetNextItem(OLVListItem itemToFind) {
            if (this.ShowGroups) {
                bool isFound = (itemToFind == null);
                foreach (ListViewGroup group in this.Groups) {
                    foreach (OLVListItem olvi in group.Items) {
                        if (isFound)
                            return olvi;
                        isFound = (itemToFind == olvi);
                    }
                }
                return null;
            } else {
                if (this.GetItemCount() == 0)
                    return null;
                if (itemToFind == null)
                    return this.GetItem(0);
                if (itemToFind.Index == this.GetItemCount() - 1)
                    return null;
                return this.GetItem(itemToFind.Index + 1);
            }
        }

        /// <summary>
        /// Return the last item in the order they are shown to the user.
        /// If the control is not grouped, the display order is the same as the
        /// sorted list order. But if the list is grouped, the display order is different.
        /// </summary>
        /// <returns></returns>
        public virtual OLVListItem GetLastItemInDisplayOrder() {
            if (!this.ShowGroups)
                return this.GetItem(this.GetItemCount() - 1);
            
            if (this.Groups.Count > 0) {
                ListViewGroup lastGroup = this.Groups[this.Groups.Count - 1];
                if (lastGroup.Items.Count > 0)
                    return (OLVListItem)lastGroup.Items[lastGroup.Items.Count - 1];
            }

            return null;
        }

        /// <summary>
        /// Return the n'th item (0-based) in the order they are shown to the user.
        /// If the control is not grouped, the display order is the same as the
        /// sorted list order. But if the list is grouped, the display order is different.
        /// </summary>
        /// <param name="n"></param>
        /// <returns></returns>
        public virtual OLVListItem GetNthItemInDisplayOrder(int n) {
            if (!this.ShowGroups)
                return this.GetItem(n);

            foreach (ListViewGroup group in this.Groups) {
                if (n < group.Items.Count)
                    return (OLVListItem)group.Items[n];

                n -= group.Items.Count;
            }

            return null;
        }

        /// <summary>
        /// Return the index of the given ListViewItem as it currently shown to the user.
        /// If the control is not grouped, the display order is the same as the
        /// sorted list order. But if the list is grouped, the display order is different.
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public virtual int GetItemIndexInDisplayOrder(ListViewItem value) {
            if (!this.ShowGroups)
                return value.Index;

            // TODO: This could be optimized
            int i = 0;
            foreach (ListViewGroup lvg in this.Groups) {
                foreach (ListViewItem lvi in lvg.Items) {
                    if (lvi == value)
                        return i;
                    i++;
                }
            }

            return -1;
        }

        /// <summary>
        /// Return the ListViewItem that appears immediately before the given item.
        /// If the given item is null, the last item in the list will be returned.
        /// Return null if the given item is the first item.
        /// </summary>
        /// <param name="itemToFind">The item that is before the item that is returned</param>
        /// <returns>A ListViewItem</returns>
        public virtual OLVListItem GetPreviousItem(OLVListItem itemToFind) {
            if (this.ShowGroups) {
                OLVListItem previousItem = null;
                foreach (ListViewGroup group in this.Groups) {
                    foreach (OLVListItem lvi in group.Items) {
                        if (lvi == itemToFind)
                            return previousItem;
                        else
                            previousItem = lvi;
                    }
                }
                if (itemToFind == null)
                    return previousItem;
                else
                    return null;
            } else {
                if (this.GetItemCount() == 0)
                    return null;
                if (itemToFind == null)
                    return this.GetItem(this.GetItemCount() - 1);
                if (itemToFind.Index == 0)
                    return null;
                return this.GetItem(itemToFind.Index - 1);
            }
        }

        /// <summary>
        /// Insert the given collection of objects before the given position
        /// </summary>
        /// <param name="index">Where to insert the objects</param>
        /// <param name="modelObjects">The objects to be inserted</param>
        /// <remarks>
        /// <para>
        /// This operation only makes sense of non-sorted, non-grouped
        /// lists, since any subsequent sort/group operation will rearrange
        /// the list.
        /// </para>
        /// <para>This method only works on ObjectListViews and FastObjectListViews.</para>
        ///</remarks>
        public virtual void InsertObjects(int index, ICollection modelObjects) {
            if (this.InvokeRequired) {
                this.Invoke((MethodInvoker)delegate() {
                    this.InsertObjects(index, modelObjects);
                });
                return;
            }
            if (modelObjects == null)
                return;

            this.BeginUpdate();
            try {
                // Give the world a chance to cancel or change the added objects
                ItemsAddingEventArgs args = new ItemsAddingEventArgs(modelObjects);
                this.OnItemsAdding(args);
                if (args.Canceled)
                    return;
                modelObjects = args.ObjectsToAdd;

                this.TakeOwnershipOfObjects();
                ArrayList ourObjects = ObjectListView.EnumerableToArray(this.Objects, false);

                // If we are filtering the list, there is no way to efficiently
                // insert the objects, so just put them into our collection and rebuild.
                if (this.IsFiltering) {
                    ourObjects.InsertRange(index, modelObjects);
                    this.BuildList(true);
                    return;
                }

                this.ListViewItemSorter = null;
                index = Math.Max(0, Math.Min(index, this.GetItemCount()));
                int i = index;
                foreach (object modelObject in modelObjects) {
                    if (modelObject != null) {
                        ourObjects.Insert(i, modelObject);
                        OLVListItem lvi = new OLVListItem(modelObject);
                        this.FillInValues(lvi, modelObject);
                        this.Items.Insert(i, lvi);
                        i++;
                    }
                }

                for (i = index; i < this.GetItemCount(); i++) {
                    OLVListItem lvi = this.GetItem(i);
                    this.SetSubItemImages(lvi.Index, lvi);
                }

                // Tell the world that the list has changed
                this.OnItemsChanged(new ItemsChangedEventArgs());
            } finally {
                this.EndUpdate();
            }
        }

        /// <summary>
        /// Return true if the row representing the given model is selected
        /// </summary>
        /// <param name="model">The model object to look for</param>
        /// <returns>Is the row selected</returns>
        public bool IsSelected(object model) {
            OLVListItem item = this.ModelToItem(model);
            if (item == null)
                return false;
            else
                return item.Selected;
        }

        /// <summary>
        /// Has the given URL been visited?
        /// </summary>
        /// <param name="url">The string to be consider</param>
        /// <returns>Has it been visited</returns>
        public virtual bool IsUrlVisited(string url) {
            return this.visitedUrlMap.ContainsKey(url);
        }

        /// <summary>
        /// Scroll the ListView by the given deltas.
        /// </summary>
        /// <param name="dx">Horizontal delta</param>
        /// <param name="dy">Vertical delta</param>
        internal void LowLevelScroll(int dx, int dy) {
            NativeMethods.Scroll(this, dx, dy);
        }

        internal Point LowLevelScrollPosition {
            get {
                return new Point(NativeMethods.GetScrollPosition(this, true), NativeMethods.GetScrollPosition(this, false));
            }
        }

        /// <summary>
        /// Remember that the given URL has been visited
        /// </summary>
        /// <param name="url">The url to be remembered</param>
        /// <remarks>This does not cause the control be redrawn</remarks>
        public virtual void MarkUrlVisited(string url) {
            this.visitedUrlMap[url] = true;
        }

        /// <summary>
        /// Move the given collection of objects to the given index.
        /// </summary>
        /// <remarks>This operation only makes sense on non-grouped ObjectListViews.</remarks>
        /// <param name="index"></param>
        /// <param name="modelObjects"></param>
        public virtual void MoveObjects(int index, ICollection modelObjects) {

            // We are going to remove all the given objects from our list
            // and then insert them at the given location
            this.TakeOwnershipOfObjects();
            ArrayList ourObjects = ObjectListView.EnumerableToArray(this.Objects, false);

            List<int> indicesToRemove = new List<int>();
            foreach (object modelObject in modelObjects) {
                if (modelObject != null) {
                    int i = this.IndexOf(modelObject);
                    if (i >= 0) {
                        indicesToRemove.Add(i);
                        ourObjects.Remove(modelObject);
                        if (i <= index)
                            index--;
                    }
                }
            }

            // Remove the objects in reverse order so earlier
            // deletes don't change the index of later ones
            indicesToRemove.Sort();
            indicesToRemove.Reverse();
            try {
                this.BeginUpdate();
                foreach (int i in indicesToRemove) {
                    this.Items.RemoveAt(i);
                }
                this.InsertObjects(index, modelObjects);
            } finally {
                this.EndUpdate();
            }
        }

        /// <summary>
        /// Calculate what item is under the given point?
        /// </summary>
        /// <param name="x"></param>
        /// <param name="y"></param>
        /// <returns></returns>
        new public ListViewHitTestInfo HitTest(int x, int y) {
            // Everything costs something. Playing with the layout of the header can cause problems
            // with the hit testing. If the header shrinks
            try {
                return base.HitTest(x, y);
            } catch (ArgumentOutOfRangeException) {
                return new ListViewHitTestInfo(null, null, ListViewHitTestLocations.None);
            }
        }

        /// <summary>
        /// What is under the given point? This takes the various parts of a cell into accout, including
        /// any custom parts that a custom renderer might use
        /// </summary>
        /// <param name="x"></param>
        /// <param name="y"></param>
        /// <returns>An information block about what is under the point</returns>
        public virtual OlvListViewHitTestInfo OlvHitTest(int x, int y) {
            ListViewHitTestInfo hitTestInfo = this.HitTest(x, y);
            OlvListViewHitTestInfo hti = new OlvListViewHitTestInfo(hitTestInfo);

            // There is a bug/"feature" of the ListView concerning hit testing.
            // If FullRowSelect is false and the point is over cell 0 but not on
            // the text or icon, HitTest will not register a hit. We could turn
            // FullRowSelect on, do the HitTest, and then turn it off again, but
            // toggling FullRowSelect in that way messes up the tooltip in the
            // underlying control. So we have to find another way.
            //
            // It's too hard to try to write the hit test from scratch. Grouping (for
            // example) makes it just too complicated. So, we have to use HitTest
            // but try to get around its limits.
            //
            // First step is to determine if the point was within column 0.
            // If it was, then we only have to determine if there is an actual row
            // under the point. If there is, then we know that the point is over cell 0.
            // So we try a Battleship-style approach: is there a subcell to the right
            // of cell 0? This will return a false negative if column 0 is the rightmost column,
            // so we also check for a subcell to the left. But if only column 0 is visible,
            // then that will fail too, so we check for something at the very left of the
            // control.
            //
            // This will still fail under pathological conditions. If column 0 fills
            // the whole listview and no part of the text column 0 is visible
            // (because it is horizontally scrolled offscreen), then the hit test will fail.

            // Are we in the buggy context? Details view, not full row select, and
            // failing to find anything
            if (hitTestInfo.Item == null && !this.FullRowSelect && this.View == View.Details) {
                // Is the point within the column 0? If it is, maybe it should have been a hit.
                // Let's test slightly to the right and then to left of column 0. Hopefully one
                // of those will hit a subitem
                Point sides = NativeMethods.GetScrolledColumnSides(this, 0);
                if (x >= sides.X && x <= sides.Y) {
                    // We look for:
                    // - any subitem to the right of cell 0?
                    // - any subitem to the left of cell 0?
                    // - cell 0 at the left edge of the screen
                    hitTestInfo = this.HitTest(sides.Y + 4, y);
                    if (hitTestInfo.Item == null)
                        hitTestInfo = this.HitTest(sides.X - 4, y);
                    if (hitTestInfo.Item == null)
                        hitTestInfo = this.HitTest(4, y);

                    if (hitTestInfo.Item != null) {
                        // We hit something! So, the original point must have been in cell 0
                        hti.Item = (OLVListItem)hitTestInfo.Item;
                        hti.SubItem = hti.Item.GetSubItem(0);
                        hti.Location = ListViewHitTestLocations.None;
                        hti.HitTestLocation = HitTestLocation.InCell;
                    }
                }
            }

            if (this.OwnerDraw)
                this.CalculateOwnerDrawnHitTest(hti, x, y);
            else
                this.CalculateStandardHitTest(hti, x, y);

            return hti;
        }

        /// <summary>
        /// Perform a hit test when the control is not owner drawn
        /// </summary>
        /// <param name="hti"></param>
        /// <param name="x"></param>
        /// <param name="y"></param>
        protected virtual void CalculateStandardHitTest(OlvListViewHitTestInfo hti, int x, int y) {

            // Standard hit test works fine for the primary column
            if (this.View != View.Details || hti.ColumnIndex == 0 ||
                hti.SubItem == null || hti.Column == null)
                return;

            Rectangle cellBounds = hti.SubItem.Bounds;
            bool hasImage = (this.GetActualImageIndex(hti.SubItem.ImageSelector) != -1);

            // Unless we say otherwise, it was an general incell hit
            hti.HitTestLocation = HitTestLocation.InCell;

            // Check if the point is over where an image should be.
            // If there is a checkbox or image there, tag it and exit.
            Rectangle r = cellBounds;
            r.Width = this.SmallImageSize.Width;
            if (r.Contains(x, y)) {
                if (hti.Column.CheckBoxes) {
                    hti.HitTestLocation = HitTestLocation.CheckBox;
                    return;
                }
                if (hasImage) {
                    hti.HitTestLocation = HitTestLocation.Image;
                    return;
                }
            }

            // Figure out where the text actually is and if the point is in it
            // The standard HitTest assumes that any point inside a subitem is
            // a hit on Text -- which is clearly not true.
            Rectangle textBounds = cellBounds;
            textBounds.X += 4;
            if (hasImage)
                textBounds.X += this.SmallImageSize.Width;

            Size proposedSize = new Size(textBounds.Width, textBounds.Height);
            Size textSize = TextRenderer.MeasureText(hti.SubItem.Text, this.Font, proposedSize, TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
            textBounds.Width = textSize.Width;

            switch (hti.Column.TextAlign) {
                case HorizontalAlignment.Center:
                    textBounds.X += (cellBounds.Right - cellBounds.Left - textSize.Width) / 2;
                    break;
                case HorizontalAlignment.Right:
                    textBounds.X = cellBounds.Right - textSize.Width;
                    break;
            }
            if (textBounds.Contains(x, y)) {
                hti.HitTestLocation = HitTestLocation.Text;
            }
        }

        /// <summary>
        /// Perform a hit test when the control is owner drawn. This hands off responsibility
        /// to the renderer.
        /// </summary>
        /// <param name="hti"></param>
        /// <param name="x"></param>
        /// <param name="y"></param>
        protected virtual void CalculateOwnerDrawnHitTest(OlvListViewHitTestInfo hti, int x, int y) {
            // If the click wasn't on an item, give up
            if (hti.Item == null)
                return;

            // If the list is showing column, but they clicked outside the columns, also give up
            if (this.View == View.Details && hti.Column == null)
                return;

            // Which renderer was responsible for drawing that point
            IRenderer renderer = null;
            if (this.View == View.Details) {
                renderer = hti.Column.Renderer ?? this.DefaultRenderer;
            } else {
                renderer = this.ItemRenderer;
            }

            // We can't decide who was responsible. Give up
            if (renderer == null)
                return;

            // Ask the responsible renderer what is at that point
            renderer.HitTest(hti, x, y);
        }

        /// <summary>
        /// Pause (or unpause) all animations in the list
        /// </summary>
        /// <param name="isPause">true to pause, false to unpause</param>
        public virtual void PauseAnimations(bool isPause) {
            for (int i = 0; i < this.Columns.Count; i++) {
                OLVColumn col = this.GetColumn(i);
                if (col.Renderer is ImageRenderer)
                    ((ImageRenderer)col.Renderer).Paused = isPause;
            }
        }

        /// <summary>
        /// Rebuild the columns based upon its current view and column visibility settings
        /// </summary>
        public virtual void RebuildColumns() {
            this.ChangeToFilteredColumns(this.View);
        }

        /// <summary>
        /// Remove the given model object from the ListView
        /// </summary>
        /// <param name="modelObject">The model to be removed</param>
        /// <remarks>See RemoveObjects() for more details
        /// <para>This method is thread-safe.</para>
        /// </remarks>
        public virtual void RemoveObject(object modelObject) {
            if (this.InvokeRequired)
                this.Invoke((MethodInvoker)delegate() { this.RemoveObject(modelObject); });
            else
                this.RemoveObjects(new object[] { modelObject });
        }

        /// <summary>
        /// Remove all of the given objects from the control.
        /// </summary>
        /// <param name="modelObjects">Collection of objects to be removed</param>
        /// <remarks>
        /// <para>Nulls and model objects that are not in the ListView are silently ignored.</para>
        /// <para>This method is thread-safe.</para>
        /// </remarks>
        public virtual void RemoveObjects(ICollection modelObjects) {
            if (this.InvokeRequired) {
                this.Invoke((MethodInvoker)delegate() { this.RemoveObjects(modelObjects); });
                return;
            }
            if (modelObjects == null)
                return;

            this.BeginUpdate();
            try {
                // Give the world a chance to cancel or change the added objects
                ItemsRemovingEventArgs args = new ItemsRemovingEventArgs(modelObjects);
                this.OnItemsRemoving(args);
                if (args.Canceled)
                    return;
                modelObjects = args.ObjectsToRemove;

                this.TakeOwnershipOfObjects();
                ArrayList ourObjects = (ArrayList)this.Objects;
                foreach (object modelObject in modelObjects) {
                    if (modelObject != null) {
                        ourObjects.Remove(modelObject);
                        int i = this.IndexOf(modelObject);
                        if (i >= 0)
                            this.Items.RemoveAt(i);
                    }
                }
                this.PostProcessRows();

                // Tell the world that the list has changed
                this.OnItemsChanged(new ItemsChangedEventArgs());
            } finally {
                this.EndUpdate();
            }
        }

        /// <summary>
        /// Select all rows in the listview
        /// </summary>
        public virtual void SelectAll() {
            NativeMethods.SelectAllItems(this);
        }

        /// <summary>
        /// Set the given image to be fixed in the bottom right of the list view.
        /// This image will not scroll when the list view scrolls.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This method uses ListView's native ability to display a background image.
        /// It has a few limitations: 
        /// </para>
        /// <list type="bullet">
        /// <item><description>It doesn't work well with owner drawn mode. In owner drawn mode, each cell draws itself,
        /// including its background, which covers the background image.</description></item>
        /// <item><description>It doesn't look very good when grid lines are enabled, since the grid lines are drawn over the image.</description></item>
        /// <item><description>It does not work at all on XP.</description></item>
        /// <item><description>Obviously, it doesn't look good when alternate row background colors are enabled.</description></item>
        /// </list>
        /// <para>
        /// If you can live with these limitations, native watermarks are quite neat. They are true backgrounds, not
        /// translucent overlays like the OverlayImage uses. They also have the decided advantage over overlays in that
        /// they work correctly even in MDI applications.
        /// </para>
        /// <para>Setting this clears any background image.</para>
        /// </remarks>
        /// <param name="image">The image to be drawn. If null, any existing image will be removed.</param>
        public void SetNativeBackgroundWatermark(Image image) {
            NativeMethods.SetBackgroundImage(this, image, true, false, 0, 0);
        }

        /// <summary>
        /// Set the given image to be background of the ListView so that it appears at the given
        /// percentage offsets within the list.
        /// </summary>
        /// <remarks>
        /// <para>This has the same limitations as described in <see cref="SetNativeBackgroundWatermark"/>. Make sure those limitations
        /// are understood before using the method.</para>
        /// <para>This is very similar to setting the <see cref="System.Windows.Forms.Control.BackgroundImage"/> property of the standard .NET ListView, except that the standard
        /// BackgroundImage does not handle images with transparent areas properly -- it renders transparent areas as black. This 
        /// method does not have that problem.</para>
        /// <para>Setting this clears any background watermark.</para>
        /// </remarks>
        /// <param name="image">The image to be drawn. If null, any existing image will be removed.</param>
        /// <param name="xOffset">The horizontal percentage where the image will be placed. 0 is absolute left, 100 is absolute right.</param>
        /// <param name="yOffset">The vertical percentage where the image will be placed.</param>
        public void SetNativeBackgroundImage(Image image, int xOffset, int yOffset) {
            NativeMethods.SetBackgroundImage(this, image, false, false, xOffset, yOffset);
        }

        /// <summary>
        /// Set the given image to be the tiled background of the ListView.
        /// </summary>
        /// <remarks>
        /// <para>This has the same limitations as described in <see cref="SetNativeBackgroundWatermark"/> and <see cref="SetNativeBackgroundImage"/>.
        /// Make sure those limitations
        /// are understood before using the method.</para>
        /// </remarks>
        /// <param name="image">The image to be drawn. If null, any existing image will be removed.</param>
        public void SetNativeBackgroundTiledImage(Image image) {
            NativeMethods.SetBackgroundImage(this, image, false, true, 0, 0);
        }

        /// <summary>
        /// Set the collection of objects that will be shown in this list view.
        /// </summary>
        /// <remark>This method can safely be called from background threads.</remark>
        /// <remarks>The list is updated immediately</remarks>
        /// <param name="collection">The objects to be displayed</param>
        public virtual void SetObjects(IEnumerable collection) {
            this.SetObjects(collection, false);
        }

        /// <summary>
        /// Set the collection of objects that will be shown in this list view.
        /// </summary>
        /// <remark>This method can safely be called from background threads.</remark>
        /// <remarks>The list is updated immediately</remarks>
        /// <param name="collection">The objects to be displayed</param>
        /// <param name="preserveState">Should the state of the list be preserved as far as is possible.</param>
        public virtual void SetObjects(IEnumerable collection, bool preserveState) {
            if (this.InvokeRequired) {
                this.Invoke((MethodInvoker)delegate { this.SetObjects(collection, preserveState); });
                return;
            }

            // Give the world a chance to cancel or change the assigned collection
            ItemsChangingEventArgs args = new ItemsChangingEventArgs(this.objects, collection);
            this.OnItemsChanging(args);
            if (args.Canceled)
                return;
            collection = args.NewObjects;

            // If we own the current list and they change to another list, we don't own it anymore
            if (this.isOwnerOfObjects && this.objects != collection)
                this.isOwnerOfObjects = false;
            this.objects = collection;
            this.BuildList(preserveState);

            // Tell the world that the list has changed
            this.OnItemsChanged(new ItemsChangedEventArgs());
        }

        #endregion

        #region Save/Restore State

        /// <summary>
        /// Return a byte array that represents the current state of the ObjectListView, such
        /// that the state can be restored by RestoreState()
        /// </summary>
        /// <remarks>
        /// <para>The state of an ObjectListView includes the attributes that the user can modify:
        /// <list type="bullet">
        /// <item><description>current view (i.e. Details, Tile, Large Icon...)</description></item>
        /// <item><description>sort column and direction</description></item>
        /// <item><description>column order</description></item>
        /// <item><description>column widths</description></item>
        /// <item><description>column visibility</description></item>
        /// </list>
        /// </para>
        /// <para>
        /// It does not include selection or the scroll position.
        /// </para>
        /// </remarks>
        /// <returns>A byte array representing the state of the ObjectListView</returns>
        public virtual byte[] SaveState() {
            ObjectListViewState olvState = new ObjectListViewState();
            olvState.VersionNumber = 1;
            olvState.NumberOfColumns = this.AllColumns.Count;
            olvState.CurrentView = this.View;

            // If we have a sort column, it is possible that it is not currently being shown, in which
            // case, it's Index will be -1. So we calculate its index directly. Technically, the sort
            // column does not even have to a member of AllColumns, in which case IndexOf will return -1,
            // which is works fine since we have no way of restoring such a column anyway.
            if (this.LastSortColumn != null)
                olvState.SortColumn = this.AllColumns.IndexOf(this.LastSortColumn);
            olvState.LastSortOrder = this.LastSortOrder;
            olvState.IsShowingGroups = this.ShowGroups;

            if (this.AllColumns.Count > 0 && this.AllColumns[0].LastDisplayIndex == -1)
                this.RememberDisplayIndicies();

            foreach (OLVColumn column in this.AllColumns) {
                olvState.ColumnIsVisible.Add(column.IsVisible);
                olvState.ColumnDisplayIndicies.Add(column.LastDisplayIndex);
                olvState.ColumnWidths.Add(column.Width);
            }

            // Now that we have stored our state, convert it to a byte array
            using (MemoryStream ms = new MemoryStream()) {
                BinaryFormatter serializer = new BinaryFormatter();
                serializer.AssemblyFormat = FormatterAssemblyStyle.Simple;
                serializer.Serialize(ms, olvState);
                return ms.ToArray();
            }
        }

        /// <summary>
        /// Restore the state of the control from the given string, which must have been
        /// produced by SaveState()
        /// </summary>
        /// <param name="state">A byte array returned from SaveState()</param>
        /// <returns>Returns true if the state was restored</returns>
        public virtual bool RestoreState(byte[] state) {
            using (MemoryStream ms = new MemoryStream(state)) {
                BinaryFormatter deserializer = new BinaryFormatter();
                ObjectListViewState olvState;
                try {
                    olvState = deserializer.Deserialize(ms) as ObjectListViewState;
                } catch (System.Runtime.Serialization.SerializationException) {
                    return false;
                }
                // The number of columns has changed. We have no way to match old
                // columns to the new ones, so we just give up.
                if (olvState.NumberOfColumns != this.AllColumns.Count)
                    return false;
                if (olvState.SortColumn == -1) {
                    this.LastSortColumn = null;
                    this.LastSortOrder = SortOrder.None;
                } else {
                    this.LastSortColumn = this.AllColumns[olvState.SortColumn];
                    this.LastSortOrder = olvState.LastSortOrder;
                }
                for (int i = 0; i < olvState.NumberOfColumns; i++) {
                    OLVColumn column = this.AllColumns[i];
                    column.Width = (int)olvState.ColumnWidths[i];
                    column.IsVisible = (bool)olvState.ColumnIsVisible[i];
                    column.LastDisplayIndex = (int)olvState.ColumnDisplayIndicies[i];
                }
                if (olvState.IsShowingGroups != this.ShowGroups)
                    this.ShowGroups = olvState.IsShowingGroups;
                if (this.View == olvState.CurrentView)
                    this.RebuildColumns();
                else
                    this.View = olvState.CurrentView;
            }

            return true;
        }

        /// <summary>
        /// Instances of this class are used to store the state of an ObjectListView.
        /// </summary>
        [Serializable]
        internal class ObjectListViewState
        {
            public int VersionNumber = 1;
            public int NumberOfColumns = 1;
            public View CurrentView;
            public int SortColumn = -1;
            public bool IsShowingGroups;
            public SortOrder LastSortOrder = SortOrder.None;
            public ArrayList ColumnIsVisible = new ArrayList();
            public ArrayList ColumnDisplayIndicies = new ArrayList();
            public ArrayList ColumnWidths = new ArrayList();
        }

        #endregion

        #region Event handlers

        /// <summary>
        /// The application is idle. Trigger a SelectionChanged event.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        protected virtual void HandleApplicationIdle(object sender, EventArgs e) {
            // Remove the handler before triggering the event
            Application.Idle -= new EventHandler(HandleApplicationIdle);
            this.hasIdleHandler = false;

            this.OnSelectionChanged(new EventArgs());
        }

        /// <summary>
        /// The application is idle. Trigger a SelectionChanged event.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        protected virtual void HandleApplicationIdle_ResizeColumns(object sender, EventArgs e) {
            // Remove the handler before triggering the event
            Application.Idle -= new EventHandler(HandleApplicationIdle_ResizeColumns);
            this.hasResizeColumnsHandler = false;

            this.ResizeFreeSpaceFillingColumns();
        }

        /// <summary>
        /// Handle the BeginScroll listview notification
        /// </summary>
        /// <param name="m"></param>
        /// <returns>True if the event was completely handled</returns>
        protected virtual bool HandleBeginScroll(ref Message m) {
            //System.Diagnostics.Debug.WriteLine("LVN_BEGINSCROLL");

            NativeMethods.NMLVSCROLL nmlvscroll = (NativeMethods.NMLVSCROLL)m.GetLParam(typeof(NativeMethods.NMLVSCROLL));
            if (nmlvscroll.dx != 0) {
                int scrollPositionH = NativeMethods.GetScrollPosition(this, true);
                ScrollEventArgs args = new ScrollEventArgs(ScrollEventType.EndScroll, scrollPositionH - nmlvscroll.dx, scrollPositionH, ScrollOrientation.HorizontalScroll);
                this.OnScroll(args);

                // Force any empty list msg to redraw when the list is scrolled horizontally
                if (this.GetItemCount() == 0)
                    this.Invalidate();
            }
            if (nmlvscroll.dy != 0) {
                int scrollPositionV = NativeMethods.GetScrollPosition(this, false);
                ScrollEventArgs args = new ScrollEventArgs(ScrollEventType.EndScroll, scrollPositionV - nmlvscroll.dy, scrollPositionV, ScrollOrientation.VerticalScroll);
                this.OnScroll(args);
            }

            return false;
        }

        /// <summary>
        /// Handle the EndScroll listview notification
        /// </summary>
        /// <param name="m"></param>
        /// <returns>True if the event was completely handled</returns>
        protected virtual bool HandleEndScroll(ref Message m) {
            //System.Diagnostics.Debug.WriteLine("LVN_BEGINSCROLL");

            // There is a bug in ListView under XP that causes the gridlines to be incorrectly scrolled
            // when the left button is clicked to scroll. This is supposedly documented at
            // KB 813791, but I couldn't find it anywhere. You can follow this thread to see the discussion
            // http://www.ureader.com/msg/1484143.aspx

            if (!ObjectListView.IsVistaOrLater && Control.MouseButtons == MouseButtons.Left && this.GridLines) {
                this.Invalidate();
                this.Update();
            }

            return false;
        }

        /// <summary>
        /// Handle the LinkClick listview notification
        /// </summary>
        /// <param name="m"></param>
        /// <returns>True if the event was completely handled</returns>
        protected virtual bool HandleLinkClick(ref Message m) {
            //System.Diagnostics.Debug.WriteLine("HandleLinkClick");

            NativeMethods.NMLVLINK nmlvlink = (NativeMethods.NMLVLINK)m.GetLParam(typeof(NativeMethods.NMLVLINK));

            // Find the group that was clicked and trigger an event
            foreach (OLVGroup x in this.OLVGroups) {
                if (x.GroupId == nmlvlink.iSubItem) {
                    this.OnGroupTaskClicked(new GroupTaskClickedEventArgs(x));
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// The cell tooltip control wants information about the tool tip that it should show.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        protected virtual void HandleCellToolTipShowing(object sender, ToolTipShowingEventArgs e) {
            this.BuildCellEvent(e, this.PointToClient(Cursor.Position));
            if (e.Item != null) {
                e.Text = this.GetCellToolTip(e.ColumnIndex, e.RowIndex);
                this.OnCellToolTip(e);
            }
        }

        /// <summary>
        /// Allow the HeaderControl to call back into HandleHeaderToolTipShowing without making that method public
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        internal void headerToolTip_Showing(object sender, ToolTipShowingEventArgs e) {
            this.HandleHeaderToolTipShowing(sender, e);
        }

        /// <summary>
        /// The header tooltip control wants information about the tool tip that it should show.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        protected virtual void HandleHeaderToolTipShowing(object sender, ToolTipShowingEventArgs e) {
            e.ColumnIndex = this.HeaderControl.ColumnIndexUnderCursor;
            if (e.ColumnIndex < 0)
                return;

            e.RowIndex = -1;
            e.Model = null;
            e.Column = this.GetColumn(e.ColumnIndex);
            e.Text = this.GetHeaderToolTip(e.ColumnIndex);
            e.ListView = this;
            this.OnHeaderToolTip(e);
        }

        /// <summary>
        /// Event handler for the column click event
        /// </summary>
        protected virtual void HandleColumnClick(object sender, ColumnClickEventArgs e) {
            if (!this.PossibleFinishCellEditing())
                return;

            // Toggle the sorting direction on successive clicks on the same column
            if (this.LastSortColumn != null && e.Column == this.LastSortColumn.Index)
                this.LastSortOrder = (this.LastSortOrder == SortOrder.Descending ? SortOrder.Ascending : SortOrder.Descending);
            else
                this.LastSortOrder = SortOrder.Ascending;

            this.BeginUpdate();
            try {
                this.Sort(e.Column);
            } finally {
                this.EndUpdate();
            }
        }

        #endregion

        #region Low level Windows Message handling

        /// <summary>
        /// Override the basic message pump for this control
        /// </summary>
        /// <param name="m"></param>
        protected override void WndProc(ref Message m) {
            //System.Diagnostics.Debug.WriteLine(m.Msg);
            switch (m.Msg) {
                case 2: // WM_DESTROY
                    if (!this.HandleDestroy(ref m))
                        base.WndProc(ref m);
                    break;
                //case 0x14: // WM_ERASEBKGND
                //    Can't do anything here since, when the control is double buffered, anything
                //    done here is immediately over-drawn
                //    break;
                case 0x0F: // WM_PAINT
                    if (!this.HandlePaint(ref m))
                        base.WndProc(ref m);
                    break;
                case 0x46: // WM_WINDOWPOSCHANGING
                    if (!this.HandleWindowPosChanging(ref m))
                        base.WndProc(ref m);
                    break;
                case 0x4E: // WM_NOTIFY
                    if (!this.HandleNotify(ref m))
                        base.WndProc(ref m);
                    break;
                case 0x0100: // WM_KEY_DOWN
                    if (!this.HandleKeyDown(ref m))
                        base.WndProc(ref m);
                    break;
                case 0x0102: // WM_CHAR
                    if (!this.HandleChar(ref m))
                        base.WndProc(ref m);
                    break;
                case 0x0201: // WM_LBUTTONDOWN
                    if (this.PossibleFinishCellEditing() && !this.HandleLButtonDown(ref m))
                        base.WndProc(ref m);
                    break;
                case 0x202:  // WM_LBUTTONUP
                    if (ObjectListView.IsVistaOrLater && this.HasCollapsibleGroups)
                        base.DefWndProc(ref m);
                    base.WndProc(ref m);
                    break;
                case 0x0203: // WM_LBUTTONDBLCLK
                    if (this.PossibleFinishCellEditing() && !this.HandleLButtonDoubleClick(ref m))
                        base.WndProc(ref m);
                    break;
                case 0x0204: // WM_RBUTTONDOWN
                    if (this.PossibleFinishCellEditing() && !this.HandleRButtonDown(ref m))
                        base.WndProc(ref m);
                    break;
                case 0x0206: // WM_RBUTTONDBLCLK
                    if (this.PossibleFinishCellEditing() && !this.HandleRButtonDoubleClick(ref m))
                        base.WndProc(ref m);
                    break;
                case 0x204E: // WM_REFLECT_NOTIFY
                    if (!this.HandleReflectNotify(ref m))
                        base.WndProc(ref m);
                    break;
                case 0x114: // WM_HSCROLL:
                case 0x115: // WM_VSCROLL:
                    if (this.PossibleFinishCellEditing())
                        base.WndProc(ref m);
                    break;
                case 0x20A: // WM_MOUSEWHEEL:
                case 0x20E: // WM_MOUSEHWHEEL:
                    if (this.PossibleFinishCellEditing())
                        base.WndProc(ref m);
                    break;
                case 0x7B: // WM_CONTEXTMENU
                    if (!this.HandleContextMenu(ref m))
                        base.WndProc(ref m);
                    break;
                default:
                    base.WndProc(ref m);
                    break;
            }
        }

        /// <summary>
        /// Handle the search for item m if possible.
        /// </summary>
        /// <param name="m">The m to be processed</param>
        /// <returns>bool to indicate if the m has been handled</returns>
        protected virtual bool HandleChar(ref Message m) {

            // Trigger a normal KeyPress event, which listeners can handle if they want.
            // Handling the event stops ObjectListView's fancy search-by-typing.
            if (this.ProcessKeyEventArgs(ref m))
                return true;

            const int MILLISECONDS_BETWEEN_KEYPRESSES = 1000;

            // What character did the user type and was it part of a longer string?
            char character = (char)m.WParam.ToInt32(); //TODO: Will this work on 64 bit or MBCS?
            if (character == (char)Keys.Back) {
                // Backspace forces the next key to be considered the start of a new search
                this.timeLastCharEvent = 0;
                return true;
            }

            if (System.Environment.TickCount < (this.timeLastCharEvent + MILLISECONDS_BETWEEN_KEYPRESSES))
                this.lastSearchString += character;
            else
                this.lastSearchString = character.ToString();

            // If this control is showing checkboxes, we want to ignore single space presses,
            // since they are used to toggle the selected checkboxes.
            if (this.CheckBoxes && this.lastSearchString == " ") {
                this.timeLastCharEvent = 0;
                return true;
            }

            // Where should the search start?
            int start = 0;
            ListViewItem focused = this.FocusedItem;
            if (focused != null) {
                start = this.GetItemIndexInDisplayOrder(focused);

                // If the user presses a single key, we search from after the focused item,
                // being careful not to march past the end of the list
                if (this.lastSearchString.Length == 1) {
                    start += 1;
                    if (start == this.GetItemCount())
                        start = 0;
                }
            }

            // Give the world a chance to fiddle with or completely avoid the searching process
            BeforeSearchingEventArgs args = new BeforeSearchingEventArgs(this.lastSearchString, start);
            this.OnBeforeSearching(args);
            if (args.Canceled)
                return true;

            // The parameters of the search may have been changed
            string searchString = args.StringToFind;
            start = args.StartSearchFrom;

            // Do the actual search
            int found = this.FindMatchingRow(searchString, start, SearchDirectionHint.Down);
            if (found >= 0) {
                // Select and focus on the found item
                this.BeginUpdate();
                try {
                    this.SelectedIndices.Clear();
                    ListViewItem lvi = this.GetNthItemInDisplayOrder(found);
                    lvi.Selected = true;
                    lvi.Focused = true;
                    this.EnsureVisible(lvi.Index);
                } finally {
                    this.EndUpdate();
                }
            }

            // Tell the world that a search has occurred
            AfterSearchingEventArgs args2 = new AfterSearchingEventArgs(searchString, found);
            this.OnAfterSearching(args2);
            if (!args2.Handled) {
                if (found < 0)
                    System.Media.SystemSounds.Beep.Play();
            }

            // When did this event occur?
            this.timeLastCharEvent = System.Environment.TickCount;
            return true;
        }
        private int timeLastCharEvent;
        private string lastSearchString;

        /// <summary>
        /// The user wants to see the context menu.
        /// </summary>
        /// <param name="m">The windows m</param>
        /// <returns>A bool indicating if this m has been handled</returns>
        /// <remarks>
        /// We want to ignore context menu requests that are triggered by right clicks on the header
        /// </remarks>
        protected virtual bool HandleContextMenu(ref Message m) {
            // Don't try to handle context menu commands at design time.
            if (this.DesignMode)
                return false;

            // If the context menu command was generated by the keyboard, LParam will be -1.
            // We don't want to process these.
            if ((m.LParam.ToInt32()) == -1)
                return false;

            // If the context menu came from somewhere other than the header control,
            // we also don't want to ignore it
            if (m.WParam != this.HeaderControl.Handle)
                return false;

            // OK. Looks like a right click in the header
            if (!this.PossibleFinishCellEditing())
                return true;

            int columnIndex = this.HeaderControl.ColumnIndexUnderCursor;
            return this.HandleHeaderRightClick(columnIndex);
        }

        /// <summary>
        /// Handle the Custom draw series of notifications
        /// </summary>
        /// <param name="m">The message</param>
        /// <returns>True if the message has been handled</returns>
        protected virtual bool HandleCustomDraw(ref Message m) {
            const int CDDS_PREPAINT = 1;
            const int CDDS_POSTPAINT = 2;
            const int CDDS_PREERASE = 3;
            const int CDDS_POSTERASE = 4;
            //const int CDRF_NEWFONT = 2;
            //const int CDRF_SKIPDEFAULT = 4;
            const int CDDS_ITEM = 0x00010000;
            const int CDDS_SUBITEM = 0x00020000;
            const int CDDS_ITEMPREPAINT = (CDDS_ITEM | CDDS_PREPAINT);
            const int CDDS_ITEMPOSTPAINT = (CDDS_ITEM | CDDS_POSTPAINT);
            const int CDDS_ITEMPREERASE = (CDDS_ITEM | CDDS_PREERASE);
            const int CDDS_ITEMPOSTERASE = (CDDS_ITEM | CDDS_POSTERASE);
            const int CDDS_SUBITEMPREPAINT = (CDDS_SUBITEM | CDDS_ITEMPREPAINT);
            const int CDDS_SUBITEMPOSTPAINT = (CDDS_SUBITEM | CDDS_ITEMPOSTPAINT);
            const int CDRF_NOTIFYPOSTPAINT = 0x10;
            //const int CDRF_NOTIFYITEMDRAW = 0x20;
            //const int CDRF_NOTIFYSUBITEMDRAW = 0x20; // same value as above!
            const int CDRF_NOTIFYPOSTERASE = 0x40;

            NativeMethods.NMLVCUSTOMDRAW nmcustomdraw = (NativeMethods.NMLVCUSTOMDRAW)m.GetLParam(typeof(NativeMethods.NMLVCUSTOMDRAW));
            //System.Diagnostics.Debug.WriteLine(String.Format("cd: {0:x}, {1}, {2}", nmcustomdraw.nmcd.dwDrawStage, nmcustomdraw.dwItemType, nmcustomdraw.nmcd.dwItemSpec));

            // There is a bug in owner drawn virtual lists which causes lots of custom draw messages
            // to be sent to the control *outside* of a WmPaint event. AFAIK, these custom draw events
            // are spurious and only serve to make the control flicker annoyingly.
            // So, we ignore messages that are outside of a paint event.
            if (!this.isInWmPaintEvent)
                return true;

            // One more complication! Sometimes with owner drawn virtual lists, the act of drawing
            // the overlays triggers a second attempt to paint the control -- which makes an annoying
            // flicker. So, we only do the custom drawing once per WmPaint event.
            if (!this.shouldDoCustomDrawing)
                return true;

            switch (nmcustomdraw.nmcd.dwDrawStage) {
                case CDDS_PREPAINT:
                    //System.Diagnostics.Debug.WriteLine("CDDS_PREPAINT");
                    // Remember which items were drawn during this paint cycle
                    if (this.prePaintLevel == 0)
                        this.drawnItems = new List<OLVListItem>();

                    // If there are any items, we have to wait until at least one has been painted
                    // before we draw the overlays. If there aren't any items, there will never be any
                    // item paint events, so we can draw the overlays whenever
                    this.isAfterItemPaint = (this.GetItemCount() == 0);
                    this.prePaintLevel++;
                    base.WndProc(ref m);

                    // Make sure that we get postpaint notifications
                    m.Result = (IntPtr)((int)m.Result | CDRF_NOTIFYPOSTPAINT | CDRF_NOTIFYPOSTERASE);
                    return true;

                case CDDS_POSTPAINT:
                    //System.Diagnostics.Debug.WriteLine("CDDS_POSTPAINT");
                    this.prePaintLevel--;

                    // When in group view, we have two problems. On XP, the control sends
                    // a whole heap of PREPAINT/POSTPAINT messages before drawing any items.
                    // We have to wait until after the first item paint before we draw overlays.
                    // On Vista, we have a different problem. On Vista, the control nests calls
                    // to PREPAINT and POSTPAINT. We only want to draw overlays on the outermost
                    // POSTPAINT.
                    if (this.prePaintLevel == 0 && (this.isMarqueSelecting || this.isAfterItemPaint)) {
                        this.shouldDoCustomDrawing = false;

                        // Draw our overlays after everything has been drawn
                        using (Graphics g = Graphics.FromHdc(nmcustomdraw.nmcd.hdc)) {
                            this.DrawAllDecorations(g, this.drawnItems);
                        }
                    }
                    break;

                case CDDS_ITEMPREPAINT:
                    //System.Diagnostics.Debug.WriteLine("CDDS_ITEMPREPAINT");

                    // When in group view on XP, the control send a whole heap of PREPAINT/POSTPAINT
                    // messages before drawing any items.
                    // We have to wait until after the first item paint before we draw overlays
                    this.isAfterItemPaint = true;

                    // This scheme of catching custom draw msgs works fine, except
                    // for Tile view. Something in .NET's handling of Tile view causes lots
                    // of invalidates and erases. So, we just ignore completely
                    // .NET's handling of Tile view and let the underlying control
                    // do its stuff. Strangely, if the Tile view is
                    // completely owner drawn, those erasures don't happen.
                    if (this.View == View.Tile) {
                        if (this.OwnerDraw && this.ItemRenderer != null)
                            base.WndProc(ref m);
                    } else {
                        base.WndProc(ref m);
                    }

                    m.Result = (IntPtr)((int)m.Result | CDRF_NOTIFYPOSTPAINT | CDRF_NOTIFYPOSTERASE);
                    return true;

                case CDDS_ITEMPOSTPAINT:
                    //System.Diagnostics.Debug.WriteLine("CDDS_ITEMPOSTPAINT");
                    if (this.Columns.Count > 0) {
                        OLVListItem olvi = this.GetItem((int)nmcustomdraw.nmcd.dwItemSpec);
                        if (olvi != null)
                            this.drawnItems.Add(olvi);
                    }
                    break;

                case CDDS_SUBITEMPREPAINT:
                    //System.Diagnostics.Debug.WriteLine(String.Format("CDDS_SUBITEMPREPAINT ({0},{1})", (int)nmcustomdraw.nmcd.dwItemSpec, nmcustomdraw.iSubItem));

                    // There is a bug in the .NET framework which appears when column 0 of an owner drawn listview
                    // is dragged to another column position.
                    // The bounds calculation always returns the left edge of column 0 as being 0.
                    // The effects of this bug become apparent
                    // when the listview is scrolled horizontally: the control can think that column 0
                    // is no longer visible (the horizontal scroll position is subtracted from the bounds, giving a
                    // rectangle that is offscreen). In those circumstances, column 0 is not redraw because
                    // the control thinks it is not visible and so does not trigger a DrawSubItem event.

                    // To fix this problem, we have to detected the situation -- owner drawing column 0 in any column except 0 --
                    // trigger our own DrawSubItem, and then prevent the default processing from occuring.

                    // Are we owner drawing column 0 when it's in any column except 0?
                    if (!this.OwnerDraw)
                        return false;

                    int columnIndex = nmcustomdraw.iSubItem;
                    if (columnIndex != 0)
                        return false;

                    int displayIndex = this.Columns[0].DisplayIndex;
                    if (displayIndex == 0)
                        return false;

                    int rowIndex = (int)nmcustomdraw.nmcd.dwItemSpec;
                    OLVListItem item = this.GetItem(rowIndex);
                    if (item == null)
                        return false;

                    // OK. We have the error condition, so lets do what the .NET framework should do.
                    // Trigger an event to draw column 0 when it is not at display index 0
                    using (Graphics g = Graphics.FromHdc(nmcustomdraw.nmcd.hdc)) {

                        // Correctly calculate the bounds of cell 0
                        Rectangle r = item.GetSubItemBounds(0);

                        // We can hardcode "0" here since we know we are only doing this for column 0
                        DrawListViewSubItemEventArgs args = new DrawListViewSubItemEventArgs(g, r, item, item.SubItems[0], rowIndex, 0,
                            this.Columns[0], (ListViewItemStates)nmcustomdraw.nmcd.uItemState);
                        this.OnDrawSubItem(args);

                        // If the event handler wants to do the default processing (i.e. DrawDefault = true), we are stuck.
                        // There is no way we can force the default drawing because of the bug in .NET we are trying to get around.
                        System.Diagnostics.Trace.Assert(!args.DrawDefault, "Default drawing is impossible in this situation");
                    }
                    m.Result = (IntPtr)4;

                    return true;

                case CDDS_SUBITEMPOSTPAINT:
                    //System.Diagnostics.Debug.WriteLine("CDDS_SUBITEMPOSTPAINT");
                    break;

                // I have included these stages, but it doesn't seem that they are sent for ListViews.
                // http://www.tech-archive.net/Archive/VC/microsoft.public.vc.mfc/2006-08/msg00220.html

                case CDDS_PREERASE:
                    //System.Diagnostics.Debug.WriteLine("CDDS_PREERASE");
                    break;

                case CDDS_POSTERASE:
                    //System.Diagnostics.Debug.WriteLine("CDDS_POSTERASE");
                    break;

                case CDDS_ITEMPREERASE:
                    //System.Diagnostics.Debug.WriteLine("CDDS_ITEMPREERASE");
                    break;

                case CDDS_ITEMPOSTERASE:
                    //System.Diagnostics.Debug.WriteLine("CDDS_ITEMPOSTERASE");
                    break;
            }

            return false;
        }
        bool isAfterItemPaint;
        List<OLVListItem> drawnItems;

        /// <summary>
        /// Handle the underlying control being destroyed
        /// </summary>
        /// <param name="m"></param>
        /// <returns></returns>
        protected virtual bool HandleDestroy(ref Message m) {
            //System.Diagnostics.Debug.WriteLine("WM_DESTROY");

            // Recreate the header control when the listview control is destroyed
            this.BeginInvoke((MethodInvoker)delegate {
                this.headerControl = null;
                this.HeaderControl.WordWrap = this.HeaderWordWrap;
            });

            // When the underlying control is destroyed, we need to recreate
            // and reconfigure its tooltip
            if (this.cellToolTip == null)
                return false;
            else {
                this.cellToolTip.PushSettings();
                base.WndProc(ref m);
                this.BeginInvoke((MethodInvoker)delegate {
                    this.UpdateCellToolTipHandle();
                    this.cellToolTip.PopSettings();
                });
                return true;
            }
        }

        /// <summary>
        /// Handle the search for item m if possible.
        /// </summary>
        /// <param name="m">The m to be processed</param>
        /// <returns>bool to indicate if the m has been handled</returns>
        protected virtual bool HandleFindItem(ref Message m) {
            // NOTE: As far as I can see, this message is never actually sent to the control, making this
            // method redundant!

            const int LVFI_STRING = 0x0002;

            NativeMethods.LVFINDINFO findInfo = (NativeMethods.LVFINDINFO)m.GetLParam(typeof(NativeMethods.LVFINDINFO));

            // We can only handle string searches
            if ((findInfo.flags & LVFI_STRING) != LVFI_STRING)
                return false;

            int start = m.WParam.ToInt32();
            m.Result = (IntPtr)this.FindMatchingRow(findInfo.psz, start, SearchDirectionHint.Down);
            return true;
        }

        /// <summary>
        /// Find the first row after the given start in which the text value in the
        /// comparison column begins with the given text. The comparison column is column 0,
        /// unless IsSearchOnSortColumn is true, in which case the current sort column is used.
        /// </summary>
        /// <param name="text">The text to be prefix matched</param>
        /// <param name="start">The index of the first row to consider</param>
        /// <param name="direction">Which direction should be searched?</param>
        /// <returns>The index of the first row that matched, or -1</returns>
        /// <remarks>The text comparison is a case-insensitive, prefix match. The search will
        /// search the every row until a match is found, wrapping at the end if needed.</remarks>
        public virtual int FindMatchingRow(string text, int start, SearchDirectionHint direction) {
            // We also can't do anything if we don't have data
            int rowCount = this.GetItemCount();
            if (rowCount == 0)
                return -1;

            // Which column are we going to use for our comparing?
            OLVColumn column = this.GetColumn(0);
            if (this.IsSearchOnSortColumn && this.View == View.Details && this.LastSortColumn != null)
                column = this.LastSortColumn;

            // Do two searches if necessary to find a match. The second search is the wrap-around part of searching
            int i;
            if (direction == SearchDirectionHint.Down) {
                i = this.FindMatchInRange(text, start, rowCount - 1, column);
                if (i == -1 && start > 0)
                    i = this.FindMatchInRange(text, 0, start - 1, column);
            } else {
                i = this.FindMatchInRange(text, start, 0, column);
                if (i == -1 && start != rowCount)
                    i = this.FindMatchInRange(text, rowCount - 1, start + 1, column);
            }

            return i;
        }

        /// <summary>
        /// Find the first row in the given range of rows that prefix matches the string value of the given column.
        /// </summary>
        /// <param name="text"></param>
        /// <param name="first"></param>
        /// <param name="last"></param>
        /// <param name="column"></param>
        /// <returns>The index of the matched row, or -1</returns>
        protected virtual int FindMatchInRange(string text, int first, int last, OLVColumn column) {
            if (first <= last) {
                for (int i = first; i <= last; i++) {
                    string data = column.GetStringValue(this.GetNthItemInDisplayOrder(i).RowObject);
                    if (data.StartsWith(text, StringComparison.CurrentCultureIgnoreCase))
                        return i;
                }
            } else {
                for (int i = first; i >= last; i--) {
                    string data = column.GetStringValue(this.GetNthItemInDisplayOrder(i).RowObject);
                    if (data.StartsWith(text, StringComparison.CurrentCultureIgnoreCase))
                        return i;
                }
            }

            return -1;
        }

        /// <summary>
        /// Handle a key down message
        /// </summary>
        /// <param name="m"></param>
        /// <returns>True if the msg has been handled</returns>
        protected virtual bool HandleKeyDown(ref Message m) {

            // If this is a checkbox list, toggle the selected rows when the user presses Space
            if (this.CheckBoxes && m.WParam.ToInt32() == (int)Keys.Space && this.SelectedIndices.Count > 0) {
                this.ToggleSelectedRowCheckBoxes();
                return true;
            }

            // Remember the scroll position so we can decide if the listview has scrolled in the
            // handling of the event.
            int scrollPositionH = NativeMethods.GetScrollPosition(this, true);
            int scrollPositionV = NativeMethods.GetScrollPosition(this, false);

            base.WndProc(ref m);

            // It's possible that the processing in base.WndProc has actually destroyed this control
            if (this.IsDisposed)
                return true;

            // If the keydown processing changed the scroll position, trigger a Scroll event
            int newScrollPositionH = NativeMethods.GetScrollPosition(this, true);
            int newScrollPositionV = NativeMethods.GetScrollPosition(this, false);

            if (scrollPositionH != newScrollPositionH) {
                ScrollEventArgs args = new ScrollEventArgs(ScrollEventType.EndScroll,
                    scrollPositionH, newScrollPositionH, ScrollOrientation.HorizontalScroll);
                this.OnScroll(args);
                this.RefreshHotItem();
            }
            if (scrollPositionV != newScrollPositionV) {
                ScrollEventArgs args = new ScrollEventArgs(ScrollEventType.EndScroll,
                    scrollPositionV, newScrollPositionV, ScrollOrientation.VerticalScroll);
                this.OnScroll(args);
                this.RefreshHotItem();
            }

            return true;
        }

        private void ToggleSelectedRowCheckBoxes() {
            // This doesn't actually toggle all rows. It toggles the first row, and
            // all other rows get the check state of that first row.
            Object primaryModel = this.GetItem(this.SelectedIndices[0]).RowObject;
            this.ToggleCheckObject(primaryModel);
            CheckState? state = this.GetCheckState(primaryModel);
            if (state.HasValue) {
                foreach (Object x in this.SelectedObjects)
                    this.SetObjectCheckedness(x, state.Value);
            }
        }

        /// <summary>
        /// Catch the Left Button down event.
        /// </summary>
        /// <param name="m">The m to be processed</param>
        /// <returns>bool to indicate if the m has been handled</returns>
        protected virtual bool HandleLButtonDown(ref Message m) {
            // We have to intercept this low level message rather than the more natural
            // overridding of OnMouseDown, since ListCtrl's internal mouse down behavior
            // is to select (or deselect) rows when the mouse is released. We don't
            // want the selection to change when the user checks or unchecks a checkbox, so if the
            // mouse down event was to check/uncheck, we have to hide this mouse
            // down event from the control.

            int x = m.LParam.ToInt32() & 0xFFFF;
            int y = (m.LParam.ToInt32() >> 16) & 0xFFFF;

            return this.ProcessLButtonDown(this.OlvHitTest(x, y));
        }

        /// <summary>
        /// Handle a left mouse down at the given hit test location
        /// </summary>
        /// <remarks>Subclasses can override this to do something unique</remarks>
        /// <param name="hti"></param>
        /// <returns>True if the message has been handled</returns>
        protected virtual bool ProcessLButtonDown(OlvListViewHitTestInfo hti) {
            if (hti.Item == null)
                return false;

            // If they didn't click checkbox, we can just return
            if (this.View != View.Details || hti.HitTestLocation != HitTestLocation.CheckBox)
                return false;

            // Did they click a sub item checkbox?
            if (hti.Column.Index > 0) {
                if (hti.Column.IsEditable)
                    this.ToggleSubItemCheckBox(hti.RowObject, hti.Column);
                return true;
            }

            // They must have clicked the primary checkbox
            this.ToggleCheckObject(hti.RowObject);

            // If they change the checkbox of a selecte row, all the rows in the selection
            // should be given the same state
            if (hti.Item.Selected) {
                CheckState? state = this.GetCheckState(hti.RowObject);
                if (state.HasValue) {
                    foreach (Object x in this.SelectedObjects)
                        this.SetObjectCheckedness(x, state.Value);
                }
            }

            return true;
        }

        /// <summary>
        /// Catch the Right Button down event.
        /// </summary>
        /// <param name="m">The m to be processed</param>
        /// <returns>bool to indicate if the m has been handled</returns>
        protected virtual bool HandleRButtonDown(ref Message m) {
            int x = m.LParam.ToInt32() & 0xFFFF;
            int y = (m.LParam.ToInt32() >> 16) & 0xFFFF;

            return this.ProcessRButtonDown(this.OlvHitTest(x, y));
        }

        /// <summary>
        /// Handle a left mouse down at the given hit test location
        /// </summary>
        /// <remarks>Subclasses can override this to do something unique</remarks>
        /// <param name="hti"></param>
        /// <returns>True if the message has been handled</returns>
        protected virtual bool ProcessRButtonDown(OlvListViewHitTestInfo hti) {
            if (hti.Item == null)
                return false;

            // Ignore clicks on checkboxes
            return (this.View == View.Details && hti.HitTestLocation == HitTestLocation.CheckBox);
        }

        /// <summary>
        /// Catch the Left Button double click event.
        /// </summary>
        /// <param name="m">The m to be processed</param>
        /// <returns>bool to indicate if the m has been handled</returns>
        protected virtual bool HandleLButtonDoubleClick(ref Message m) {
            int x = m.LParam.ToInt32() & 0xFFFF;
            int y = (m.LParam.ToInt32() >> 16) & 0xFFFF;

            return this.ProcessLButtonDoubleClick(this.OlvHitTest(x, y));
        }

        /// <summary>
        /// Handle a mouse double click at the given hit test location
        /// </summary>
        /// <remarks>Subclasses can override this to do something unique</remarks>
        /// <param name="hti"></param>
        /// <returns>True if the message has been handled</returns>
        protected virtual bool ProcessLButtonDoubleClick(OlvListViewHitTestInfo hti) {

            // If the user double clicked on a checkbox, ignore it
            return (hti.HitTestLocation == HitTestLocation.CheckBox);
        }

        /// <summary>
        /// Catch the right Button double click event.
        /// </summary>
        /// <param name="m">The m to be processed</param>
        /// <returns>bool to indicate if the m has been handled</returns>
        protected virtual bool HandleRButtonDoubleClick(ref Message m) {
            int x = m.LParam.ToInt32() & 0xFFFF;
            int y = (m.LParam.ToInt32() >> 16) & 0xFFFF;

            return this.ProcessRButtonDoubleClick(this.OlvHitTest(x, y));
        }

        /// <summary>
        /// Handle a right mouse double click at the given hit test location
        /// </summary>
        /// <remarks>Subclasses can override this to do something unique</remarks>
        /// <param name="hti"></param>
        /// <returns>True if the message has been handled</returns>
        protected virtual bool ProcessRButtonDoubleClick(OlvListViewHitTestInfo hti) {

            // If the user double clicked on a checkbox, ignore it
            return (hti.HitTestLocation == HitTestLocation.CheckBox);
        }

        /// <summary>
        /// In the notification messages, we handle change of state of list items
        /// </summary>
        /// <param name="m">The m to be processed</param>
        /// <returns>bool to indicate if the m has been handled</returns>
        protected virtual bool HandleReflectNotify(ref Message m) {
            const int NM_CLICK = -2;
            const int NM_DBLCLK = -3;
            const int NM_RDBLCLK = -6;
            const int NM_CUSTOMDRAW = -12;
            const int NM_RELEASEDCAPTURE = -16;
            const int LVN_ITEMCHANGED = -101;
            const int LVN_ITEMCHANGING = -100;
            const int LVN_MARQUEEBEGIN = -156;
            const int LVN_GETINFOTIP = -158;
            const int LVN_BEGINSCROLL = -180;
            const int LVN_ENDSCROLL = -181;
            const int LVN_LINKCLICK = -184;
            const int LVIF_STATE = 8;

            bool isMsgHandled = false;

            // TODO: Don't do any logic in this method. Create separate methods for each message

            NativeMethods.NMHDR nmhdr = (NativeMethods.NMHDR)m.GetLParam(typeof(NativeMethods.NMHDR));
            //System.Diagnostics.Debug.WriteLine(String.Format("rn: {0}", nmhdr->code));

            switch (nmhdr.code) {
                case NM_CLICK:
                    // The standard ListView does some strange stuff here when the list has checkboxes.
                    // If you shift click on non-primary columns when FullRowSelect is true, the 
                    // checkedness of the selected rows changes. 
                    // We avoid all that by just saying we've handled this message.
                    //System.Diagnostics.Debug.WriteLine("NM_CLICK");
                    isMsgHandled = true;
                    this.OnClick(EventArgs.Empty);
                    break;

                case LVN_BEGINSCROLL:
                    isMsgHandled = this.HandleBeginScroll(ref m);
                    break;

                case LVN_ENDSCROLL:
                    isMsgHandled = this.HandleEndScroll(ref m);
                    break;

                case LVN_LINKCLICK:
                    isMsgHandled = this.HandleLinkClick(ref m);
                    break;

                case LVN_MARQUEEBEGIN:
                    //System.Diagnostics.Debug.WriteLine("LVN_MARQUEEBEGIN");
                    this.isMarqueSelecting = true;
                    break;

                case LVN_GETINFOTIP:
                    //System.Diagnostics.Debug.WriteLine("LVN_GETINFOTIP");
                    // When virtual lists are in SmallIcon view, they generates tooltip message with invalid item indicies.
                    NativeMethods.NMLVGETINFOTIP nmGetInfoTip = (NativeMethods.NMLVGETINFOTIP)m.GetLParam(typeof(NativeMethods.NMLVGETINFOTIP));
                    isMsgHandled = nmGetInfoTip.iItem >= this.GetItemCount();
                    break;

                case NM_RELEASEDCAPTURE:
                    //System.Diagnostics.Debug.WriteLine("NM_RELEASEDCAPTURE");
                    this.isMarqueSelecting = false;
                    this.Invalidate();
                    break;

                case NM_CUSTOMDRAW:
                    //System.Diagnostics.Debug.WriteLine("NM_CUSTOMDRAW");
                    isMsgHandled = this.HandleCustomDraw(ref m);
                    break;

                case NM_DBLCLK:
                    // The default behavior of a .NET ListView with checkboxes is to toggle the checkbox on
                    // double-click. That's just silly, if you ask me :)
                    if (this.CheckBoxes) {
                        // How do we make ListView not do that silliness? We could just ignore the message
                        // but the last part of the base code sets up state information, and without that
                        // state, the ListView doesn't trigger MouseDoubleClick events. So we fake a
                        // right button double click event, which sets up the same state, but without
                        // toggling the checkbox.
                        nmhdr.code = NM_RDBLCLK;
                        Marshal.StructureToPtr(nmhdr, m.LParam, false);
                    }
                    break;

                case LVN_ITEMCHANGED:
                    //System.Diagnostics.Debug.WriteLine("LVN_ITEMCHANGED");
                    NativeMethods.NMLISTVIEW nmlistviewPtr2 = (NativeMethods.NMLISTVIEW)m.GetLParam(typeof(NativeMethods.NMLISTVIEW));
                    if ((nmlistviewPtr2.uChanged & LVIF_STATE) != 0) {
                        CheckState currentValue = this.CalculateCheckState(nmlistviewPtr2.uOldState);
                        CheckState newCheckValue = this.CalculateCheckState(nmlistviewPtr2.uNewState);
                        if (currentValue != newCheckValue) {
                            // Remove the state indicies so that we don't trigger the OnItemChecked method
                            // when we call our base method after exiting this method
                            nmlistviewPtr2.uOldState = (nmlistviewPtr2.uOldState & 0x0FFF);
                            nmlistviewPtr2.uNewState = (nmlistviewPtr2.uNewState & 0x0FFF);
                            Marshal.StructureToPtr(nmlistviewPtr2, m.LParam, false);
                        }
                    }
                    break;

                case LVN_ITEMCHANGING:
                    //System.Diagnostics.Debug.WriteLine("LVN_ITEMCHANGING");
                    NativeMethods.NMLISTVIEW nmlistviewPtr = (NativeMethods.NMLISTVIEW)m.GetLParam(typeof(NativeMethods.NMLISTVIEW));
                    if ((nmlistviewPtr.uChanged & LVIF_STATE) != 0) {
                        CheckState currentValue = this.CalculateCheckState(nmlistviewPtr.uOldState);
                        CheckState newCheckValue = this.CalculateCheckState(nmlistviewPtr.uNewState);

                        if (currentValue != newCheckValue) {
                            // Prevent the base method from seeing the state change,
                            // since we handled it elsewhere
                            nmlistviewPtr.uChanged &= ~LVIF_STATE;
                            Marshal.StructureToPtr(nmlistviewPtr, m.LParam, false);
                        }
                    }
                    break;
            }

            return isMsgHandled;
        }

        private CheckState CalculateCheckState(int state) {
            switch ((state & 0xf000) >> 12) {
                case 1:
                    return CheckState.Unchecked;
                case 2:
                    return CheckState.Checked;
                case 3:
                    return CheckState.Indeterminate;
                default:
                    return CheckState.Checked;
            }
        }

        /// <summary>
        /// In the notification messages, we handle attempts to change the width of our columns
        /// </summary>
        /// <param name="m">The m to be processed</param>
        /// <returns>bool to indicate if the m has been handled</returns>
        protected bool HandleNotify(ref Message m) {
            bool isMsgHandled = false;

            const int NM_CUSTOMDRAW = -12;

            const int HDN_FIRST = (0 - 300);
            const int HDN_ITEMCHANGINGA = (HDN_FIRST - 0);
            const int HDN_ITEMCHANGINGW = (HDN_FIRST - 20);
            const int HDN_ITEMCLICKA = (HDN_FIRST - 2);
            const int HDN_ITEMCLICKW = (HDN_FIRST - 22);
            const int HDN_DIVIDERDBLCLICKA = (HDN_FIRST - 5);
            const int HDN_DIVIDERDBLCLICKW = (HDN_FIRST - 25);
            const int HDN_BEGINTRACKA = (HDN_FIRST - 6);
            const int HDN_BEGINTRACKW = (HDN_FIRST - 26);
            const int HDN_ENDTRACKA = (HDN_FIRST - 7);
            const int HDN_ENDTRACKW = (HDN_FIRST - 27);
            const int HDN_TRACKA = (HDN_FIRST - 8);
            const int HDN_TRACKW = (HDN_FIRST - 28);

            // Handle the notification, remembering to handle both ANSI and Unicode versions
            NativeMethods.NMHEADER nmheader = (NativeMethods.NMHEADER)m.GetLParam(typeof(NativeMethods.NMHEADER));
            //System.Diagnostics.Debug.WriteLine(String.Format("not: {0}", nmhdr->code));

            //if (nmhdr.code < HDN_FIRST)
            //    System.Diagnostics.Debug.WriteLine(nmhdr.code);

            // In KB Article #183258, MS states that when a header control has the HDS_FULLDRAG style, it will receive
            // ITEMCHANGING events rather than TRACK events. Under XP SP2 (at least) this is not always true, which may be
            // why MS has withdrawn that particular KB article. It is true that the header is always given the HDS_FULLDRAG
            // style. But even while window style set, the control doesn't always received ITEMCHANGING events.
            // The controlling setting seems to be the Explorer option "Show Window Contents While Dragging"!
            // In the category of "truly bizarre side effects", if the this option is turned on, we will receive
            // ITEMCHANGING events instead of TRACK events. But if it is turned off, we receive lots of TRACK events and
            // only one ITEMCHANGING event at the very end of the process.
            // If we receive HDN_TRACK messages, it's harder to control the resizing process. If we return a result of 1, we
            // cancel the whole drag operation, not just that particular track event, which is clearly not what we want.
            // If we are willing to compile with unsafe code enabled, we can modify the size of the column in place, using the
            // commented out code below. But without unsafe code, the best we can do is allow the user to drag the column to
            // any width, and then spring it back to within bounds once they release the mouse button. UI-wise it's very ugly.
            switch (nmheader.nhdr.code) {

                case NM_CUSTOMDRAW:
                    if (!this.OwnerDrawnHeader)
                        isMsgHandled = this.HeaderControl.HandleHeaderCustomDraw(ref m);
                    break;

                case HDN_ITEMCLICKA:
                case HDN_ITEMCLICKW:
                    if (!this.PossibleFinishCellEditing()) {
                        m.Result = (IntPtr)1; // prevent the change from happening
                        isMsgHandled = true;
                    }
                    break;

                case HDN_DIVIDERDBLCLICKA:
                case HDN_DIVIDERDBLCLICKW:
                case HDN_BEGINTRACKA:
                case HDN_BEGINTRACKW:
                    if (!this.PossibleFinishCellEditing()) {
                        m.Result = (IntPtr)1; // prevent the change from happening
                        isMsgHandled = true;
                        break;
                    }
                    if (nmheader.iItem >= 0 && nmheader.iItem < this.Columns.Count) {
                        OLVColumn column = this.GetColumn(nmheader.iItem);
                        // Space filling columns can't be dragged or double-click resized
                        if (column.FillsFreeSpace) {
                            m.Result = (IntPtr)1; // prevent the change from happening
                            isMsgHandled = true;
                        }
                    }
                    break;
                case HDN_ENDTRACKA:
                case HDN_ENDTRACKW:
                    if (this.ShowGroups)
                        this.ResizeLastGroup();
                    break;
                case HDN_TRACKA:
                case HDN_TRACKW:
                    if (nmheader.iItem >= 0 && nmheader.iItem < this.Columns.Count) {
                        NativeMethods.HDITEM hditem = (NativeMethods.HDITEM)Marshal.PtrToStructure(nmheader.pHDITEM, typeof(NativeMethods.HDITEM));
                        OLVColumn column = this.GetColumn(nmheader.iItem);
                        if (hditem.cxy < column.MinimumWidth)
                            hditem.cxy = column.MinimumWidth;
                        else if (column.MaximumWidth != -1 && hditem.cxy > column.MaximumWidth)
                            hditem.cxy = column.MaximumWidth;
                        Marshal.StructureToPtr(hditem, nmheader.pHDITEM, false);
                    }
                    break;

                case HDN_ITEMCHANGINGA:
                case HDN_ITEMCHANGINGW:
                    nmheader = (NativeMethods.NMHEADER)m.GetLParam(typeof(NativeMethods.NMHEADER));
                    if (nmheader.iItem >= 0 && nmheader.iItem < this.Columns.Count) {
                        NativeMethods.HDITEM hditem = (NativeMethods.HDITEM)Marshal.PtrToStructure(nmheader.pHDITEM, typeof(NativeMethods.HDITEM));
                        OLVColumn column = this.GetColumn(nmheader.iItem);
                        // Check the mask to see if the width field is valid, and if it is, make sure it's within range
                        if ((hditem.mask & 1) == 1) {
                            if (hditem.cxy < column.MinimumWidth ||
                                (column.MaximumWidth != -1 && hditem.cxy > column.MaximumWidth)) {
                                m.Result = (IntPtr)1; // prevent the change from happening
                                isMsgHandled = true;
                            }
                        }
                    }
                    break;

                case ToolTipControl.TTN_SHOW:
                    //System.Diagnostics.Debug.WriteLine("olv TTN_SHOW");
                    System.Diagnostics.Trace.Assert(this.CellToolTip.Handle == nmheader.nhdr.hwndFrom);
                    isMsgHandled = this.CellToolTip.HandleShow(ref m);
                    break;

                case ToolTipControl.TTN_POP:
                    //System.Diagnostics.Debug.WriteLine("olv TTN_POP");
                    System.Diagnostics.Trace.Assert(this.CellToolTip.Handle == nmheader.nhdr.hwndFrom);
                    isMsgHandled = this.CellToolTip.HandlePop(ref m);
                    break;

                case ToolTipControl.TTN_GETDISPINFO:
                    //System.Diagnostics.Debug.WriteLine("olv TTN_GETDISPINFO");
                    System.Diagnostics.Trace.Assert(this.CellToolTip.Handle == nmheader.nhdr.hwndFrom);
                    isMsgHandled = this.CellToolTip.HandleGetDispInfo(ref m);
                    break;
            }

            return isMsgHandled;
        }

        /// <summary>
        /// Create a ToolTipControl to manage the tooltip control used by the listview control
        /// </summary>
        protected virtual void CreateCellToolTip() {
            this.cellToolTip = new ToolTipControl();
            this.cellToolTip.AssignHandle(NativeMethods.GetTooltipControl(this));
            this.cellToolTip.Showing += new EventHandler<ToolTipShowingEventArgs>(HandleCellToolTipShowing);
            this.cellToolTip.SetMaxWidth();
            NativeMethods.MakeTopMost(this.cellToolTip);
        }

        /// <summary>
        /// Update the handle used by our cell tooltip to be the tooltip used by
        /// the underlying Windows listview control.
        /// </summary>
        protected virtual void UpdateCellToolTipHandle() {
            if (this.cellToolTip != null && this.cellToolTip.Handle == IntPtr.Zero)
                this.cellToolTip.AssignHandle(NativeMethods.GetTooltipControl(this));
        }

        /// <summary>
        /// Handle the WM_PAINT event
        /// </summary>
        /// <param name="m"></param>
        /// <returns>Return true if the msg has been handled and nothing further should be done</returns>
        protected virtual bool HandlePaint(ref Message m) {
            //System.Diagnostics.Debug.WriteLine("> WMPAINT");

            // We only want to custom draw the control within WmPaint message and only
            // once per paint event. We use these bools to insure this.
            this.isInWmPaintEvent = true;
            this.shouldDoCustomDrawing = true;
            this.prePaintLevel = 0;

            this.ShowOverlays();

            this.HandlePrePaint();
            base.WndProc(ref m);
            this.HandlePostPaint();
            this.isInWmPaintEvent = false;
            //System.Diagnostics.Debug.WriteLine("< WMPAINT");
            return true;
        }
        private int prePaintLevel;

        /// <summary>
        /// Perform any steps needed before painting the control
        /// </summary>
        protected virtual void HandlePrePaint() {
            // When we get a WM_PAINT msg, remember the rectangle that is being updated.
            // We can't get this information later, since the BeginPaint call wipes it out.
            this.lastUpdateRectangle = NativeMethods.GetUpdateRect(this);

            //// When the list is empty, we want to handle the drawing of the control by ourselves.
            //// Unfortunately, there is no easy way to tell our superclass that we want to do this.
            //// So we resort to guile and deception. We validate the list area of the control, which
            //// effectively tells our superclass that this area does not need to be painted.
            //// Our superclass will then not paint the control, leaving us free to do so ourselves.
            //// Without doing this trickery, the superclass will draw the list as empty,
            //// and then moments later, we will draw the empty list msg, giving a nasty flicker
            //if (this.GetItemCount() == 0 && this.HasEmptyListMsg)
            //    NativeMethods.ValidateRect(this, this.ClientRectangle);
        }

        /// <summary>
        /// Perform any steps needed after painting the control
        /// </summary>
        protected virtual void HandlePostPaint() {
            // This message is no longer necessary, but we keep it for compatibility
        }

        /// <summary>
        /// Handle the window position changing.
        /// </summary>
        /// <param name="m">The m to be processed</param>
        /// <returns>bool to indicate if the m has been handled</returns>
        protected virtual bool HandleWindowPosChanging(ref Message m) {
            const int SWP_NOSIZE = 1;

            NativeMethods.WINDOWPOS pos = (NativeMethods.WINDOWPOS)m.GetLParam(typeof(NativeMethods.WINDOWPOS));
            if ((pos.flags & SWP_NOSIZE) == 0) {
                if (pos.cx < this.Bounds.Width) // only when shrinking
                    // pos.cx is the window width, not the client area width, so we have to subtract the border widths
                    this.ResizeFreeSpaceFillingColumns(pos.cx - (this.Bounds.Width - this.ClientSize.Width));
                if (this.ShowGroups)
                    this.ResizeLastGroup();
            }

            return false;
        }

        #endregion

        #region Column header clicking, column hiding and resizing

        /// <summary>
        /// The user has right clicked on the column headers. Do whatever is required
        /// </summary>
        /// <returns>Return true if this event has been handle</returns>
        protected virtual bool HandleHeaderRightClick(int columnIndex) {
            ColumnClickEventArgs eventArgs = new ColumnClickEventArgs(columnIndex);
            this.OnColumnRightClick(eventArgs);

            return this.ShowHeaderRightClickMenu(columnIndex, Cursor.Position);
        }

        /// <summary>
        /// Show a menu that is appropriate when the given column header is clicked.
        /// </summary>
        /// <param name="columnIndex">The index of the header that was clicked. This
        /// can be -1, indicating that the header was clicked outside of a column</param>
        /// <param name="pt">Where should the menu be shown</param>
        /// <returns>True if a menu was displayed</returns>
        protected virtual bool ShowHeaderRightClickMenu(int columnIndex, Point pt) {
            ToolStripDropDown m = this.MakeHeaderRightClickMenu(columnIndex);
            if (m.Items.Count > 0) {
                m.Show(pt);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Create the menu that should be displayed when the user right clicks
        /// on the given column header.
        /// </summary>
        /// <param name="columnIndex">Index of the column that was right clicked.
        /// This can be negative, which indicates a click outside of any header.</param>
        /// <returns>The toolstrip that should be displayed</returns>
        protected virtual ToolStripDropDown MakeHeaderRightClickMenu(int columnIndex) {
            ToolStripDropDown m = new ContextMenuStrip();

            if (columnIndex >= 0 && this.UseFiltering && this.ShowFilterMenuOnRightClick)
                m = this.MakeFilteringMenu(m, columnIndex);

            if (columnIndex >= 0 && this.ShowCommandMenuOnRightClick) 
                m = this.MakeColumnCommandMenu(m, columnIndex);

            if (this.SelectColumnsOnRightClickBehaviour != ColumnSelectBehaviour.None) {
                m = this.MakeColumnSelectMenu(m);
            }

            return m;
        }

        /// <summary>
        /// The user has right clicked on the column headers. Do whatever is required
        /// </summary>
        /// <returns>Return true if this event has been handle</returns>
        [Obsolete("Use HandleHeaderRightClick(int) instead")]
        protected virtual bool HandleHeaderRightClick() {
            return false;
        }

        /// <summary>
        /// Show a popup menu at the given point which will allow the user to choose which columns
        /// are visible on this listview
        /// </summary>
        /// <param name="pt">Where should the menu be placed</param>
        [Obsolete("Use ShowHeaderRightClickMenu instead")]
        protected virtual void ShowColumnSelectMenu(Point pt) {
            ToolStripDropDown m = this.MakeColumnSelectMenu(new ContextMenuStrip());
            m.Show(pt);
        }

        /// <summary>
        /// Show a popup menu at the given point which will allow the user to choose which columns
        /// are visible on this listview
        /// </summary>
        /// <param name="columnIndex"></param>
        /// <param name="pt">Where should the menu be placed</param>
        [Obsolete("Use ShowHeaderRightClickMenu instead")]
        protected virtual void ShowColumnCommandMenu(int columnIndex, Point pt) {
            ToolStripDropDown m = this.MakeColumnCommandMenu(new ContextMenuStrip(), columnIndex);
            if (this.SelectColumnsOnRightClick) {
                if (m.Items.Count > 0)
                    m.Items.Add(new ToolStripSeparator());
                this.MakeColumnSelectMenu(m);
            }
            m.Show(pt);
        }

        /// <summary>
        /// Gets or set the text to be used for the sorting ascending command
        /// </summary>
        [Category("Labels - ObjectListView"), DefaultValue("Sort ascending by '{0}'"), Localizable(true)]
        public string MenuLabelSortAscending {
            get { return this.menuLabelSortAscending; }
            set { this.menuLabelSortAscending = value; }
        }
        private string menuLabelSortAscending = "Sort ascending by '{0}'";

        /// <summary>
        /// 
        /// </summary>
        [Category("Labels - ObjectListView"), DefaultValue("Sort descending by '{0}'"), Localizable(true)]
        public string MenuLabelSortDescending {
            get { return this.menuLabelSortDescending; }
            set { this.menuLabelSortDescending = value; }
        }
        private string menuLabelSortDescending = "Sort descending by '{0}'";

        /// <summary>
        /// 
        /// </summary>
        [Category("Labels - ObjectListView"), DefaultValue("Group by '{0}'"), Localizable(true)]
        public string MenuLabelGroupBy {
            get { return this.menuLabelGroupBy; }
            set { this.menuLabelGroupBy = value; }
        }
        private string menuLabelGroupBy = "Group by '{0}'";

        /// <summary>
        /// 
        /// </summary>
        [Category("Labels - ObjectListView"), DefaultValue("Lock grouping on '{0}'"), Localizable(true)]
        public string MenuLabelLockGroupingOn {
            get { return this.menuLabelLockGroupingOn; }
            set { this.menuLabelLockGroupingOn = value; }
        }
        private string menuLabelLockGroupingOn = "Lock grouping on '{0}'";

        /// <summary>
        /// 
        /// </summary>
        [Category("Labels - ObjectListView"), DefaultValue("Unlock grouping from '{0}'"), Localizable(true)]
        public string MenuLabelUnlockGroupingOn {
            get { return this.menuLabelUnlockGroupingOn; }
            set { this.menuLabelUnlockGroupingOn = value; }
        }
        private string menuLabelUnlockGroupingOn = "Unlock grouping from '{0}'";

        /// <summary>
        /// 
        /// </summary>
        [Category("Labels - ObjectListView"), DefaultValue("Turn off groups"), Localizable(true)]
        public string MenuLabelTurnOffGroups {
            get { return this.menuLabelTurnOffGroups; }
            set { this.menuLabelTurnOffGroups = value; }
        }
        private string menuLabelTurnOffGroups = "Turn off groups";

        /// <summary>
        /// 
        /// </summary>
        [Category("Labels - ObjectListView"), DefaultValue("Unsort"), Localizable(true)]
        public string MenuLabelUnsort {
            get { return this.menuLabelUnsort; }
            set { this.menuLabelUnsort = value; }
        }
        private string menuLabelUnsort = "Unsort";

        /// <summary>
        /// 
        /// </summary>
        [Category("Labels - ObjectListView"), DefaultValue("Columns"), Localizable(true)]
        public string MenuLabelColumns {
            get { return this.menuLabelColumns; }
            set { this.menuLabelColumns = value; }
        }
        private string menuLabelColumns = "Columns";

        /// <summary>
        /// 
        /// </summary>
        [Category("Labels - ObjectListView"), DefaultValue("Select Columns..."), Localizable(true)]
        public string MenuLabelSelectColumns {
            get { return this.menuLabelSelectColumns; }
            set { this.menuLabelSelectColumns = value; }
        }
        private string menuLabelSelectColumns = "Select Columns...";

        /// <summary>
        /// Gets or sets the image that will be place next to the Sort Ascending command
        /// </summary>
        static public Bitmap SortAscendingImage = BrightIdeasSoftware.Properties.Resources.SortAscending;

        /// <summary>
        /// Gets or sets the image that will be placed next to the Sort Descending command
        /// </summary>
        static public Bitmap SortDescendingImage = BrightIdeasSoftware.Properties.Resources.SortDescending;

        /// <summary>
        /// Append the column selection menu items to the given menu strip.
        /// </summary>
        /// <param name="strip">The menu to which the items will be added.</param>
        /// <param name="columnIndex"></param>
        /// <returns>Return the menu to which the items were added</returns>
        public virtual ToolStripDropDown MakeColumnCommandMenu(ToolStripDropDown strip, int columnIndex) {
            OLVColumn column = this.GetColumn(columnIndex);
            if (column == null)
                return strip;

            if (strip.Items.Count > 0)
                strip.Items.Add(new ToolStripSeparator());

            string label = String.Format(this.MenuLabelSortAscending, column.Text);
            if (column.Sortable && !String.IsNullOrEmpty(label)) {
                strip.Items.Add(label, ObjectListView.SortAscendingImage, (EventHandler)delegate(object sender, EventArgs args) {
                    this.Sort(column, SortOrder.Ascending);
                });
            }
            label = String.Format(this.MenuLabelSortDescending, column.Text);
            if (column.Sortable && !String.IsNullOrEmpty(label)) {
                strip.Items.Add(label, ObjectListView.SortDescendingImage, (EventHandler)delegate(object sender, EventArgs args) {
                    this.Sort(column, SortOrder.Descending);
                });
            }
            if (this.CanShowGroups) {
                label = String.Format(this.MenuLabelGroupBy, column.Text);
                if (column.Groupable && !String.IsNullOrEmpty(label)) {
                    strip.Items.Add(label, null, (EventHandler)delegate(object sender, EventArgs args) {
                        this.ShowGroups = true;
                        this.PrimarySortColumn = column;
                        this.PrimarySortOrder = SortOrder.Ascending;
                        this.BuildList();
                    });
                }
            }
            if (this.ShowGroups) {
                if (this.AlwaysGroupByColumn == column) {
                    label = String.Format(this.MenuLabelUnlockGroupingOn, column.Text);
                    if (!String.IsNullOrEmpty(label)) {
                        strip.Items.Add(label, null, (EventHandler)delegate(object sender, EventArgs args) {
                            this.AlwaysGroupByColumn = null;
                            this.AlwaysGroupBySortOrder = SortOrder.Ascending;
                            this.BuildList();
                        });
                    }
                } else {
                    label = String.Format(this.MenuLabelLockGroupingOn, column.Text);
                    if (column.Groupable && !String.IsNullOrEmpty(label)) {
                        strip.Items.Add(label, null, (EventHandler)delegate(object sender, EventArgs args) {
                            this.ShowGroups = true;
                            this.AlwaysGroupByColumn = column;
                            this.AlwaysGroupBySortOrder = SortOrder.Ascending;
                            this.BuildList();
                        });
                    }
                }
                label = String.Format(this.MenuLabelTurnOffGroups, column.Text);
                if (!String.IsNullOrEmpty(label)) {
                    strip.Items.Add(label, null, (EventHandler)delegate(object sender, EventArgs args) {
                        this.ShowGroups = false;
                        this.BuildList();
                    });
                }
            } else {
                label = String.Format(this.MenuLabelUnsort, column.Text);
                if (column.Sortable && !String.IsNullOrEmpty(label) && this.PrimarySortOrder != SortOrder.None) {
                    strip.Items.Add(label, null, (EventHandler)delegate(object sender, EventArgs args) {
                        this.Unsort();
                    });
                }
            }

            return strip;
        }

        /// <summary>
        /// Append the column selection menu items to the given menu strip.
        /// </summary>
        /// <param name="strip">The menu to which the items will be added.</param>
        /// <returns>Return the menu to which the items were added</returns>
        public virtual ToolStripDropDown MakeColumnSelectMenu(ToolStripDropDown strip) {

            System.Diagnostics.Debug.Assert(this.SelectColumnsOnRightClickBehaviour != ColumnSelectBehaviour.None);

            // Append a separator if the menu isn't empty and the last item isn't already a separator
            if (strip.Items.Count > 0 && (!(strip.Items[strip.Items.Count-1] is ToolStripSeparator)))
                strip.Items.Add(new ToolStripSeparator());

            if (this.AllColumns.Count > 0 && this.AllColumns[0].LastDisplayIndex == -1)
                this.RememberDisplayIndicies();

            if (this.SelectColumnsOnRightClickBehaviour == ColumnSelectBehaviour.ModelDialog) {
                strip.Items.Add(this.MenuLabelSelectColumns, null, delegate(object sender, EventArgs args) {
                    (new ColumnSelectionForm()).OpenOn(this);
                });
            }

            if (this.SelectColumnsOnRightClickBehaviour == ColumnSelectBehaviour.Submenu) {
                ToolStripMenuItem menu = new ToolStripMenuItem(this.MenuLabelColumns);
                menu.DropDownItemClicked += new ToolStripItemClickedEventHandler(ColumnSelectMenu_ItemClicked);
                strip.Items.Add(menu);
                this.AddItemsToColumnSelectMenu(menu.DropDownItems);
            }

            if (this.SelectColumnsOnRightClickBehaviour == ColumnSelectBehaviour.InlineMenu) {
                strip.ItemClicked += new ToolStripItemClickedEventHandler(ColumnSelectMenu_ItemClicked);
                strip.Closing += new ToolStripDropDownClosingEventHandler(ColumnSelectMenu_Closing);
                this.AddItemsToColumnSelectMenu(strip.Items);
            }

            return strip;
        }

        /// <summary>
        /// Create the menu items that will allow columns to be choosen and add them to the 
        /// given collection
        /// </summary>
        /// <param name="items"></param>
        protected void AddItemsToColumnSelectMenu(ToolStripItemCollection items) {

            // Sort columns by display order
            List<OLVColumn> columns = new List<OLVColumn>(this.AllColumns);
            columns.Sort(delegate(OLVColumn x, OLVColumn y) { return (x.LastDisplayIndex - y.LastDisplayIndex); });

            // Build menu from sorted columns
            foreach (OLVColumn col in columns) {
                ToolStripMenuItem mi = new ToolStripMenuItem(col.Text);
                mi.Checked = col.IsVisible;
                mi.Tag = col;

                // The 'Index' property returns -1 when the column is not visible, so if the
                // column isn't visible we have to enable the item. Also the first column can't be turned off
                mi.Enabled = !col.IsVisible || col.CanBeHidden;
                items.Add(mi);
            }
        }

        private void ColumnSelectMenu_ItemClicked(object sender, ToolStripItemClickedEventArgs e) {
            this.contextMenuStaysOpen = false;
            ToolStripMenuItem menuItemClicked = (ToolStripMenuItem)e.ClickedItem;
            OLVColumn col = menuItemClicked.Tag as OLVColumn;
            if (col == null)
                return;
            menuItemClicked.Checked = !menuItemClicked.Checked;
            col.IsVisible = menuItemClicked.Checked;
            this.contextMenuStaysOpen = this.SelectColumnsMenuStaysOpen;
            this.BeginInvoke(new MethodInvoker(this.RebuildColumns));
        }
        internal bool contextMenuStaysOpen;

        private void ColumnSelectMenu_Closing(object sender, ToolStripDropDownClosingEventArgs e) {
            e.Cancel = this.contextMenuStaysOpen && e.CloseReason == ToolStripDropDownCloseReason.ItemClicked;
            this.contextMenuStaysOpen = false;
        }

        /// <summary>
        /// Create a Filtering menu
        /// </summary>
        /// <param name="strip"></param>
        /// <param name="columnIndex"></param>
        /// <returns></returns>
        public virtual ToolStripDropDown MakeFilteringMenu(ToolStripDropDown strip, int columnIndex) {
            OLVColumn column = this.GetColumn(columnIndex);
            if (column == null)
                return strip;

            FilterMenuBuilder strategy = this.FilterMenuBuildStrategy;
            if (strategy == null)
                return strip;

            return strategy.MakeFilterMenu(strip, this, column);
        }

        /// <summary>
        /// Override the OnColumnReordered method to do what we want
        /// </summary>
        /// <param name="e"></param>
        protected override void OnColumnReordered(ColumnReorderedEventArgs e) {
            base.OnColumnReordered(e);

            // The internal logic of the .NET code behind a ENDDRAG event means that,
            // at this point, the DisplayIndex's of the columns are not yet as they are
            // going to be. So we have to invoke a method to run later that will remember
            // what the real DisplayIndex's are.
            this.BeginInvoke(new MethodInvoker(this.RememberDisplayIndicies));
        }

        private void RememberDisplayIndicies() {
            // Remember the display indexes so we can put them back at a later date
            foreach (OLVColumn x in this.AllColumns)
                x.LastDisplayIndex = x.DisplayIndex;
        }

        /// <summary>
        /// When the column widths are changing, resize the space filling columns
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        protected virtual void HandleColumnWidthChanging(object sender, ColumnWidthChangingEventArgs e) {
            if (this.UpdateSpaceFillingColumnsWhenDraggingColumnDivider && !this.GetColumn(e.ColumnIndex).FillsFreeSpace) {
                // If the width of a column is increasing, resize any space filling columns allowing the extra
                // space that the new column width is going to consume
                int oldWidth = this.GetColumn(e.ColumnIndex).Width;
                if (e.NewWidth > oldWidth)
                    this.ResizeFreeSpaceFillingColumns(this.ClientSize.Width - (e.NewWidth - oldWidth));
                else
                    this.ResizeFreeSpaceFillingColumns();
            }
        }

        /// <summary>
        /// When the column widths change, resize the space filling columns
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        protected virtual void HandleColumnWidthChanged(object sender, ColumnWidthChangedEventArgs e) {
            if (!this.GetColumn(e.ColumnIndex).FillsFreeSpace)
                this.ResizeFreeSpaceFillingColumns();
        }

        /// <summary>
        /// When the size of the control changes, we have to resize our space filling columns.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        protected virtual void HandleLayout(object sender, LayoutEventArgs e) {
            // We have to delay executing the recalculation of the columns, since virtual lists
            // get terribly confused if we resize the column widths during this event.
            if (!this.hasResizeColumnsHandler) {
                this.hasResizeColumnsHandler = true;
                Application.Idle += new EventHandler(HandleApplicationIdle_ResizeColumns);
            }
        }

        /// <summary>
        /// Resize our space filling columns so they fill any unoccupied width in the control
        /// </summary>
        protected virtual void ResizeFreeSpaceFillingColumns() {
            this.ResizeFreeSpaceFillingColumns(this.ClientSize.Width);
        }

        /// <summary>
        /// Resize our space filling columns so they fill any unoccupied width in the control
        /// </summary>
        protected virtual void ResizeFreeSpaceFillingColumns(int freeSpace) {
            // It's too confusing to dynamically resize columns at design time.
            if (this.DesignMode)
                return;

            if (this.Frozen)
                return;

            // Calculate the free space available
            int totalProportion = 0;
            List<OLVColumn> spaceFillingColumns = new List<OLVColumn>();
            for (int i = 0; i < this.Columns.Count; i++) {
                OLVColumn col = this.GetColumn(i);
                if (col.FillsFreeSpace) {
                    spaceFillingColumns.Add(col);
                    totalProportion += col.FreeSpaceProportion;
                } else
                    freeSpace -= col.Width;
            }
            freeSpace = Math.Max(0, freeSpace);

            // Any space filling column that would hit it's Minimum or Maximum
            // width must be treated as a fixed column.
            foreach (OLVColumn col in spaceFillingColumns.ToArray()) {
                int newWidth = (freeSpace * col.FreeSpaceProportion) / totalProportion;

                if (col.MinimumWidth != -1 && newWidth < col.MinimumWidth)
                    newWidth = col.MinimumWidth;
                else if (col.MaximumWidth != -1 && newWidth > col.MaximumWidth)
                    newWidth = col.MaximumWidth;
                else
                    newWidth = 0;

                if (newWidth > 0) {
                    col.Width = newWidth;
                    freeSpace -= newWidth;
                    totalProportion -= col.FreeSpaceProportion;
                    spaceFillingColumns.Remove(col);
                }
            }

            // Distribute the free space between the columns
            foreach (OLVColumn col in spaceFillingColumns) {
                col.Width = (freeSpace * col.FreeSpaceProportion) / totalProportion;
            }
        }

        #endregion

        #region Checkboxes

        /// <summary>
        /// Mark the given object as indeterminate check state
        /// </summary>
        /// <param name="modelObject">The model object to be marked indeterminate</param>
        public virtual void CheckIndeterminateObject(object modelObject) {
            this.SetObjectCheckedness(modelObject, CheckState.Indeterminate);
        }

        /// <summary>
        /// Mark the given object as checked in the list
        /// </summary>
        /// <param name="modelObject">The model object to be checked</param>
        public virtual void CheckObject(object modelObject) {
            this.SetObjectCheckedness(modelObject, CheckState.Checked);
        }

        /// <summary>
        /// Mark the given objects as checked in the list
        /// </summary>
        /// <param name="modelObjects">The model object to be checked</param>
        public virtual void CheckObjects(IEnumerable modelObjects) {
            foreach (object model in modelObjects)
                this.CheckObject(model);
        }

        /// <summary>
        /// Put a check into the check box at the given cell
        /// </summary>
        /// <param name="rowObject"></param>
        /// <param name="column"></param>
        public virtual void CheckSubItem(object rowObject, OLVColumn column) {
            if (column == null || rowObject == null || !column.CheckBoxes)
                return;

            column.PutCheckState(rowObject, CheckState.Checked);
            this.RefreshObject(rowObject);
        }

        /// <summary>
        /// Put an indeterminate check into the check box at the given cell
        /// </summary>
        /// <param name="rowObject"></param>
        /// <param name="column"></param>
        public virtual void CheckIndeterminateSubItem(object rowObject, OLVColumn column) {
            if (column == null || rowObject == null || !column.CheckBoxes)
                return;

            column.PutCheckState(rowObject, CheckState.Indeterminate);
            this.RefreshObject(rowObject);
        }

        /// <summary>
        /// Return true of the given object is checked
        /// </summary>
        /// <param name="modelObject">The model object whose checkedness is returned</param>
        /// <returns>Is the given object checked?</returns>
        /// <remarks>If the given object is not in the list, this method returns false.</remarks>
        public virtual bool IsChecked(object modelObject) {
            OLVListItem olvi = this.ModelToItem(modelObject);
            if (olvi == null)
                return false;
            else
                return olvi.CheckState == CheckState.Checked;
        }

        /// <summary>
        /// Return true of the given object is indeterminately checked
        /// </summary>
        /// <param name="modelObject">The model object whose checkedness is returned</param>
        /// <returns>Is the given object indeterminately checked?</returns>
        /// <remarks>If the given object is not in the list, this method returns false.</remarks>
        public virtual bool IsCheckedIndeterminate(object modelObject) {
            OLVListItem olvi = this.ModelToItem(modelObject);
            if (olvi == null)
                return false;
            else
                return (olvi.CheckState == CheckState.Indeterminate);
        }

        /// <summary>
        /// Is there a check at the check box at the given cell
        /// </summary>
        /// <param name="rowObject"></param>
        /// <param name="column"></param>
        public virtual bool IsSubItemChecked(object rowObject, OLVColumn column) {
            if (column != null && rowObject != null && column.CheckBoxes)
                return (column.GetCheckState(rowObject) == CheckState.Checked);
            else
                return false;
        }

        /// <summary>
        /// Get the checkedness of an object from the model. Returning null means the
        /// model does not know and the value from the control will be used.
        /// </summary>
        /// <param name="modelObject"></param>
        /// <returns></returns>
        protected virtual CheckState? GetCheckState(Object modelObject) {
            if (this.CheckStateGetter == null)
                return null;
            else
                return this.CheckStateGetter(modelObject);
        }

        /// <summary>
        /// Record the change of checkstate for the given object in the model.
        /// This does not update the UI -- only the model
        /// </summary>
        /// <param name="modelObject"></param>
        /// <param name="state"></param>
        /// <returns>The check state that was recorded and that should be used to update
        /// the control.</returns>
        protected virtual CheckState PutCheckState(Object modelObject, CheckState state) {
            if (this.CheckStatePutter == null)
                return state;
            else
                return this.CheckStatePutter(modelObject, state);
        }

        /// <summary>
        /// Change the check state of the given object to be the given state.
        /// </summary>
        /// <remarks>
        /// If the given model object isn't in the list, we still try to remember
        /// its state, in case it is referenced in the future.</remarks>
        /// <param name="modelObject"></param>
        /// <param name="state"></param>
        protected virtual void SetObjectCheckedness(object modelObject, CheckState state) {
            OLVListItem olvi = this.ModelToItem(modelObject);

            // If we didn't find the given, we still try to record the check state.
            if (olvi == null) {
                this.PutCheckState(modelObject, state);
                return;
            }

            if (olvi.CheckState == state)
                return;

            // Trigger checkbox changing event. We only need to do this for virtual
            // lists, since setting CheckState triggers these events for non-virtual lists
            ItemCheckEventArgs ice = new ItemCheckEventArgs(olvi.Index, state, olvi.CheckState);
            this.OnItemCheck(ice);
            if (ice.NewValue == olvi.CheckState)
                return;

            olvi.CheckState = this.PutCheckState(modelObject, state);
            this.RefreshItem(olvi);

            // Trigger check changed event
            this.OnItemChecked(new ItemCheckedEventArgs(olvi));
        }

        /// <summary>
        /// Toggle the checkedness of the given object. A checked object becomes
        /// unchecked; an unchecked or indeterminate object becomes checked.
        /// If the list has tristate checkboxes, the order is:
        ///    unchecked -> checked -> indeterminate -> unchecked ...
        /// </summary>
        /// <param name="modelObject">The model object to be checked</param>
        public virtual void ToggleCheckObject(object modelObject) {
            OLVListItem olvi = this.ModelToItem(modelObject);
            if (olvi == null)
                return;

            CheckState newState = CheckState.Checked;

            if (olvi.CheckState == CheckState.Checked) {
                if (this.TriStateCheckBoxes)
                    newState = CheckState.Indeterminate;
                else
                    newState = CheckState.Unchecked;
            } else {
                if (olvi.CheckState == CheckState.Indeterminate && this.TriStateCheckBoxes)
                    newState = CheckState.Unchecked;
            }
            this.SetObjectCheckedness(modelObject, newState);
        }

        /// <summary>
        /// Toggle the check at the check box of the given cell
        /// </summary>
        /// <param name="rowObject"></param>
        /// <param name="column"></param>
        public virtual void ToggleSubItemCheckBox(object rowObject, OLVColumn column) {
            CheckState currentState = column.GetCheckState(rowObject);
            CheckState newState = CalculateToggledCheckState(column, currentState);

            SubItemCheckingEventArgs args = new SubItemCheckingEventArgs(column, this.ModelToItem(rowObject), column.Index, currentState, newState);
            this.OnSubItemChecking(args);
            if (args.Canceled)
                return;

            switch (args.NewValue) {
                case CheckState.Checked:
                    this.CheckSubItem(rowObject, column);
                    break;
                case CheckState.Indeterminate:
                    this.CheckIndeterminateSubItem(rowObject, column);
                    break;
                case CheckState.Unchecked:
                    this.UncheckSubItem(rowObject, column);
                    break;
            }
        }

        CheckState CalculateToggledCheckState(OLVColumn column, CheckState currentState) {
            switch (currentState) {
                case CheckState.Checked: return column.TriStateCheckBoxes ? CheckState.Indeterminate : CheckState.Unchecked;
                case CheckState.Indeterminate: return CheckState.Unchecked;
                case CheckState.Unchecked: 
                default: return CheckState.Checked;
            }
        }

        /// <summary>
        /// Mark the given object as unchecked in the list
        /// </summary>
        /// <param name="modelObject">The model object to be unchecked</param>
        public virtual void UncheckObject(object modelObject) {
            this.SetObjectCheckedness(modelObject, CheckState.Unchecked);
        }


        /// <summary>
        /// Mark the given objects as unchecked in the list
        /// </summary>
        /// <param name="modelObjects">The model object to be checked</param>
        public virtual void UncheckObjects(IEnumerable modelObjects) {
            foreach (object model in modelObjects)
                this.UncheckObject(model);
        }

        /// <summary>
        /// Uncheck the check at the given cell
        /// </summary>
        /// <param name="rowObject"></param>
        /// <param name="column"></param>
        public virtual void UncheckSubItem(object rowObject, OLVColumn column) {
            if (column == null || rowObject == null || !column.CheckBoxes)
                return;

            column.PutCheckState(rowObject, CheckState.Unchecked);
            this.RefreshObject(rowObject);
        }

        #endregion

        #region OLV accessing

        /// <summary>
        /// Return the column at the given index
        /// </summary>
        /// <param name="index">Index of the column to be returned</param>
        /// <returns>An OLVColumn</returns>
        public virtual OLVColumn GetColumn(int index) {
            return (OLVColumn)this.Columns[index];
        }

        /// <summary>
        /// Return the column at the given title.
        /// </summary>
        /// <param name="name">Name of the column to be returned</param>
        /// <returns>An OLVColumn</returns>
        public virtual OLVColumn GetColumn(string name) {
            foreach (ColumnHeader column in this.Columns) {
                if (column.Text == name)
                    return (OLVColumn)column;
            }
            return null;
        }

        /// <summary>
        /// Return a collection of columns that are visible to the given view.
        /// Only Tile and Details have columns; all other views have 0 columns.
        /// </summary>
        /// <param name="view">Which view are the columns being calculate for?</param>
        /// <returns>A list of columns</returns>
        public virtual List<OLVColumn> GetFilteredColumns(View view) {
            // For both detail and tile view, the first column must be included. Normally, we would
            // use the ColumnHeader.Index property, but if the header is not currently part of a ListView
            // that property returns -1. So, we track the index of
            // the column header, and always include the first header.

            switch (view) {
                case View.Details:
                case View.Tile:
                    int index = 0;
                    return this.AllColumns.FindAll(delegate(OLVColumn x) {
                        return (index++ == 0) || x.IsVisible;
                    });
                //return this.AllColumns.FindAll(delegate(OLVColumn x) { return (index++ == 0) || x.IsTileViewColumn; });
                default:
                    return new List<OLVColumn>();
            }
        }

        /// <summary>
        /// Return the number of items in the list
        /// </summary>
        /// <returns>the number of items in the list</returns>
        public virtual int GetItemCount() {
            return this.Items.Count;
        }

        /// <summary>
        /// Return the item at the given index
        /// </summary>
        /// <param name="index">Index of the item to be returned</param>
        /// <returns>An OLVListItem</returns>
        public virtual OLVListItem GetItem(int index) {
            if (index >= 0 && index < this.GetItemCount())
                return (OLVListItem)this.Items[index];
            else
                return null;
        }

        /// <summary>
        /// Return the model object at the given index
        /// </summary>
        /// <param name="index">Index of the model object to be returned</param>
        /// <returns>A model object</returns>
        public virtual object GetModelObject(int index) {
            OLVListItem item = this.GetItem(index);
            if (item == null)
                return null;
            else
                return item.RowObject;
        }

        /// <summary>
        /// Find the item and column that are under the given co-ords
        /// </summary>
        /// <param name="x">X co-ord</param>
        /// <param name="y">Y co-ord</param>
        /// <param name="selectedColumn">The column under the given point</param>
        /// <returns>The item under the given point. Can be null.</returns>
        public virtual OLVListItem GetItemAt(int x, int y, out OLVColumn selectedColumn) {
            selectedColumn = null;
            ListViewHitTestInfo info = this.HitTest(x, y);
            if (info.Item == null)
                return null;

            if (info.SubItem != null) {
                int subItemIndex = info.Item.SubItems.IndexOf(info.SubItem);
                selectedColumn = this.GetColumn(subItemIndex);
            }

            return (OLVListItem)info.Item;
        }

        /// <summary>
        /// Return the sub item at the given index/column
        /// </summary>
        /// <param name="index">Index of the item to be returned</param>
        /// <param name="columnIndex">Index of the subitem to be returned</param>
        /// <returns>An OLVListSubItem</returns>
        public virtual OLVListSubItem GetSubItem(int index, int columnIndex) {
            OLVListItem olvi = this.GetItem(index);
            if (olvi == null)
                return null;
            else
                return olvi.GetSubItem(columnIndex);
        }

        #endregion

        #region Object manipulation

        /// <summary>
        /// Ensure that the given model object is visible
        /// </summary>
        /// <param name="modelObject">The model object to be revealed</param>
        public virtual void EnsureModelVisible(Object modelObject) {
            int index = this.IndexOf(modelObject);
            if (index >= 0)
                this.EnsureVisible(index);
        }

        /// <summary>
        /// Return the model object of the row that is selected or null if there is no selection or more than one selection
        /// </summary>
        /// <returns>Model object or null</returns>
        [Obsolete("Use SelectedObject property instead of this method")]
        public virtual object GetSelectedObject() {
            return this.SelectedObject;
        }

        /// <summary>
        /// Return the model objects of the rows that are selected or an empty collection if there is no selection
        /// </summary>
        /// <returns>ArrayList</returns>
        [Obsolete("Use SelectedObjects property instead of this method")]
        public virtual ArrayList GetSelectedObjects() {
            return ObjectListView.EnumerableToArray(this.SelectedObjects, false);
        }

        /// <summary>
        /// Return the model object of the row that is checked or null if no row is checked
        /// or more than one row is checked
        /// </summary>
        /// <returns>Model object or null</returns>
        /// <remarks>Use CheckedObject property instead of this method</remarks>
        [Obsolete("Use CheckedObject property instead of this method")]
        public virtual object GetCheckedObject() {
            return this.CheckedObject;
        }

        /// <summary>
        /// Get the collection of model objects that are checked.
        /// </summary>
        /// <remarks>Use CheckedObjects property instead of this method</remarks>
        [Obsolete("Use CheckedObjects property instead of this method")]
        public virtual ArrayList GetCheckedObjects() {
            return ObjectListView.EnumerableToArray(this.CheckedObjects, false);
        }

        /// <summary>
        /// Find the given model object within the listview and return its index
        /// </summary>
        /// <param name="modelObject">The model object to be found</param>
        /// <returns>The index of the object. -1 means the object was not present</returns>
        public virtual int IndexOf(Object modelObject) {
            for (int i = 0; i < this.GetItemCount(); i++) {
                if (this.GetModelObject(i).Equals(modelObject))
                    return i;
            }
            return -1;
        }

        /// <summary>
        /// Update the ListViewItem with the data from its associated model.
        /// </summary>
        /// <remarks>This method does not resort or regroup the view. It simply updates
        /// the displayed data of the given item</remarks>
        public virtual void RefreshItem(OLVListItem olvi) {
            olvi.UseItemStyleForSubItems = true;
            olvi.SubItems.Clear();
            this.FillInValues(olvi, olvi.RowObject);
            this.PostProcessOneRow(olvi.Index, this.GetItemIndexInDisplayOrder(olvi), olvi);
        }

        /// <summary>
        /// Update the rows that are showing the given objects
        /// </summary>
        /// <remarks>This method does not resort or regroup the view.</remarks>
        public virtual void RefreshObject(object modelObject) {
            this.RefreshObjects(new object[] { modelObject });
        }

        /// <summary>
        /// Update the rows that are showing the given objects
        /// </summary>
        /// <remarks>
        /// <para>This method does not resort or regroup the view.</para>
        /// <para>This method can safely be called from background threads.</para>
        /// </remarks>
        public virtual void RefreshObjects(IList modelObjects) {
            if (this.InvokeRequired) {
                this.Invoke((MethodInvoker)delegate { this.RefreshObjects(modelObjects); });
                return;
            }
            foreach (object modelObject in modelObjects) {
                OLVListItem olvi = this.ModelToItem(modelObject);
                if (olvi != null)
                    this.RefreshItem(olvi);
            }
        }

        /// <summary>
        /// Update the rows that are selected
        /// </summary>
        /// <remarks>This method does not resort or regroup the view.</remarks>
        public virtual void RefreshSelectedObjects() {
            foreach (ListViewItem lvi in this.SelectedItems)
                this.RefreshItem((OLVListItem)lvi);
        }

        /// <summary>
        /// Scroll the listview so that the given group is at the top.
        /// </summary>
        /// <param name="lvg">The group to be revealed</param>
        /// <remarks><para>
        /// If the group is already visible, the list will still be scrolled to move
        /// the group to the top, if that is possible.
        /// </para>
        /// <para>This only works when the list is showing groups (obviously)</para>
        /// </remarks>
        public virtual void EnsureGroupVisible(ListViewGroup lvg) {
            if (!this.ShowGroups || lvg == null)
                return;

            int groupIndex = this.Groups.IndexOf(lvg);
            if (groupIndex <= 0) {
                // There is no easy way to scroll back to the beginning of the list
                int delta = 0 - NativeMethods.GetScrollPosition(this, false);
                NativeMethods.Scroll(this, 0, delta);
            } else {
                // Find the display rectangle of the last item in the previous group
                ListViewGroup previousGroup = this.Groups[groupIndex - 1];
                ListViewItem lastItemInGroup = previousGroup.Items[previousGroup.Items.Count - 1];
                Rectangle r = this.GetItemRect(lastItemInGroup.Index);

                // Scroll so that the last item of the previous group is just out of sight,
                // which will make the desired group header visible.
                int delta = r.Y + r.Height / 2;
                NativeMethods.Scroll(this, 0, delta);
            }
        }

        /// <summary>
        /// Select the row that is displaying the given model object, in addition to any current selection.
        /// </summary>
        /// <param name="modelObject">The object to be selected</param>
        /// <remarks>Use the <see cref="SelectedObject"/> property to deselect all other rows</remarks>
        public virtual void SelectObject(object modelObject) {
            this.SelectObject(modelObject, false);
        }

        /// <summary>
        /// Select the row that is displaying the given model object, in addition to any current selection.
        /// </summary>
        /// <param name="modelObject">The object to be selected</param>
        /// <param name="setFocus">Should the object be focused as well?</param>
        /// <remarks>Use the <see cref="SelectedObject"/> property to deselect all other rows</remarks>
        public virtual void SelectObject(object modelObject, bool setFocus) {
            OLVListItem olvi = this.ModelToItem(modelObject);
            if (olvi != null) {
                olvi.Selected = true;
                if (setFocus)
                    olvi.Focused = true;
            }
        }

        /// <summary>
        /// Select the rows that is displaying any of the given model object. All other rows are deselected.
        /// </summary>
        /// <param name="modelObjects">A collection of model objects</param>
        public virtual void SelectObjects(IList modelObjects) {
            this.SelectedIndices.Clear();

            if (modelObjects == null)
                return;

            foreach (object modelObject in modelObjects) {
                OLVListItem olvi = this.ModelToItem(modelObject);
                if (olvi != null)
                    olvi.Selected = true;
            }
        }

        #endregion

        #region Freezing

        /// <summary>
        /// Freeze the listview so that it no longer updates itself.
        /// </summary>
        /// <remarks>Freeze()/Unfreeze() calls nest correctly</remarks>
        public virtual void Freeze() {
            freezeCount++;
            this.OnFreezing(new FreezeEventArgs(freezeCount));
        }

        /// <summary>
        /// Unfreeze the listview. If this call is the outermost Unfreeze(),
        /// the contents of the listview will be rebuilt.
        /// </summary>
        /// <remarks>Freeze()/Unfreeze() calls nest correctly</remarks>
        public virtual void Unfreeze() {
            if (freezeCount <= 0)
                return;

            freezeCount--;
            if (freezeCount == 0)
                DoUnfreeze();

            this.OnFreezing(new FreezeEventArgs(freezeCount));
        }

        /// <summary>
        /// Do the actual work required when the listview is unfrozen
        /// </summary>
        protected virtual void DoUnfreeze() {
            this.ResizeFreeSpaceFillingColumns();
            this.BuildList();
        }

        #endregion

        #region Column sorting

        /// <summary>
        /// Sort the items by the last sort column and order
        /// </summary>
        new public void Sort() {
            this.Sort(this.LastSortColumn, this.LastSortOrder);
        }

        /// <summary>
        /// Sort the items in the list view by the values in the given column and the last sort order
        /// </summary>
        /// <param name="columnToSortName">The name of the column whose values will be used for the sorting</param>
        public virtual void Sort(string columnToSortName) {
            this.Sort(this.GetColumn(columnToSortName), this.LastSortOrder);
        }

        /// <summary>
        /// Sort the items in the list view by the values in the given column and the last sort order
        /// </summary>
        /// <param name="columnToSortIndex">The index of the column whose values will be used for the sorting</param>
        public virtual void Sort(int columnToSortIndex) {
            if (columnToSortIndex >= 0 && columnToSortIndex < this.Columns.Count)
                this.Sort(this.GetColumn(columnToSortIndex), this.LastSortOrder);
        }

        /// <summary>
        /// Sort the items in the list view by the values in the given column and the last sort order
        /// </summary>
        /// <param name="columnToSort">The column whose values will be used for the sorting</param>
        public virtual void Sort(OLVColumn columnToSort) {
            if (this.InvokeRequired) {
                this.Invoke((MethodInvoker)delegate { this.Sort(columnToSort); });
            } else {
                this.Sort(columnToSort, this.LastSortOrder);
            }
        }

        /// <summary>
        /// Sort the items in the list view by the values in the given column and by the given order.
        /// </summary>
        /// <param name="columnToSort">The column whose values will be used for the sorting.
        /// If null, the first column will be used.</param>
        /// <param name="order">The ordering to be used for sorting. If this is None,
        /// this.Sorting and then SortOrder.Ascending will be used</param>
        /// <remarks>If ShowGroups is true, the rows will be grouped by the given column.
        /// If AlwaysGroupsByColumn is not null, the rows will be grouped by that column,
        /// and the rows within each group will be sorted by the given column.</remarks>
        public virtual void Sort(OLVColumn columnToSort, SortOrder order) {
            if (this.InvokeRequired) {
                this.Invoke((MethodInvoker)delegate { this.Sort(columnToSort, order); });
            } else {
                this.DoSort(columnToSort, order);
                this.PostProcessRows();
            }
        }

        private void DoSort(OLVColumn columnToSort, SortOrder order) {
            // Sanity checks
            if (this.GetItemCount() == 0 || this.Columns.Count == 0)
                return;

            // Fill in default values, if the parameters don't make sense
            if (this.ShowGroups) {
                columnToSort = columnToSort ?? this.GetColumn(0);
                if (order == SortOrder.None) {
                    order = this.Sorting;
                    if (order == SortOrder.None)
                        order = SortOrder.Ascending;
                }
            }

            // Give the world a chance to fiddle with or completely avoid the sorting process
            BeforeSortingEventArgs args = this.BuildBeforeSortingEventArgs(columnToSort, order);
            this.OnBeforeSorting(args);
            if (args.Canceled)
                return;

            // Virtual lists don't preserve selection, so we have to do it specifically
            // THINK: Do we need to preserve focus too?
            IList selection = new ArrayList();
            if (this.VirtualMode)
                selection = this.SelectedObjects;

            this.ClearHotItem();

            // Finally, do the work of sorting, unless an event handler has already done the sorting for us
            if (!args.Handled) {
                // Sanity checks
                if (args.ColumnToSort != null && args.SortOrder != SortOrder.None) {
                    if (this.ShowGroups)
                        this.BuildGroups(args.ColumnToGroupBy, args.GroupByOrder, args.ColumnToSort, args.SortOrder,
                            args.SecondaryColumnToSort, args.SecondarySortOrder);
                    else if (this.CustomSorter != null)
                        this.CustomSorter(columnToSort, order);
                    else
                        this.ListViewItemSorter = new ColumnComparer(args.ColumnToSort, args.SortOrder,
                            args.SecondaryColumnToSort, args.SecondarySortOrder);
                }
            }

            if (this.ShowSortIndicators)
                this.ShowSortIndicator(args.ColumnToSort, args.SortOrder);

            this.LastSortColumn = args.ColumnToSort;
            this.LastSortOrder = args.SortOrder;

            if (selection.Count > 0)
                this.SelectedObjects = selection;

            this.RefreshHotItem();

            this.OnAfterSorting(new AfterSortingEventArgs(args));
        }

        /// <summary>
        /// Put a sort indicator next to the text of the sort column
        /// </summary>
        public virtual void ShowSortIndicator() {
            if (this.ShowSortIndicators && this.LastSortOrder != SortOrder.None)
                this.ShowSortIndicator(this.LastSortColumn, this.LastSortOrder);
        }

        /// <summary>
        /// Put a sort indicator next to the text of the given given column
        /// </summary>
        /// <param name="columnToSort">The column to be marked</param>
        /// <param name="sortOrder">The sort order in effect on that column</param>
        protected virtual void ShowSortIndicator(OLVColumn columnToSort, SortOrder sortOrder) {
            int imageIndex = -1;

            if (!NativeMethods.HasBuiltinSortIndicators()) {
                // If we can't use builtin image, we have to make and then locate the index of the
                // sort indicator we want to use. SortOrder.None doesn't show an image.
                if (this.SmallImageList == null || !this.SmallImageList.Images.ContainsKey(SORT_INDICATOR_UP_KEY))
                    MakeSortIndicatorImages();

                if (sortOrder == SortOrder.Ascending)
                    imageIndex = this.SmallImageList.Images.IndexOfKey(SORT_INDICATOR_UP_KEY);
                else if (sortOrder == SortOrder.Descending)
                    imageIndex = this.SmallImageList.Images.IndexOfKey(SORT_INDICATOR_DOWN_KEY);
            }

            // Set the image for each column
            for (int i = 0; i < this.Columns.Count; i++) {
                if (columnToSort != null && i == columnToSort.Index)
                    NativeMethods.SetColumnImage(this, i, sortOrder, imageIndex);
                else
                    NativeMethods.SetColumnImage(this, i, SortOrder.None, -1);
            }
        }

        /// <summary>
        /// The name of the image used when a column is sorted ascending
        /// </summary>
        /// <remarks>This image is only used on pre-XP systems. System images are used for XP and later</remarks>
        public const string SORT_INDICATOR_UP_KEY = "sort-indicator-up";

        /// <summary>
        /// The name of the image used when a column is sorted descending
        /// </summary>
        /// <remarks>This image is only used on pre-XP systems. System images are used for XP and later</remarks>
        public const string SORT_INDICATOR_DOWN_KEY = "sort-indicator-down";

        /// <summary>
        /// If the sort indicator images don't already exist, this method will make and install them
        /// </summary>
        protected virtual void MakeSortIndicatorImages() {
            // Don't mess with the image list in design mode
            if (this.DesignMode)
                return;

            ImageList il = this.SmallImageList;
            if (il == null) {
                il = new ImageList();
                il.ImageSize = new Size(16, 16);
                il.ColorDepth = ColorDepth.Depth32Bit;
            }

            // This arrangement of points works well with (16,16) images, and OK with others
            int midX = il.ImageSize.Width / 2;
            int midY = (il.ImageSize.Height / 2) - 1;
            int deltaX = midX - 2;
            int deltaY = deltaX / 2;

            if (il.Images.IndexOfKey(SORT_INDICATOR_UP_KEY) == -1) {
                Point pt1 = new Point(midX - deltaX, midY + deltaY);
                Point pt2 = new Point(midX, midY - deltaY - 1);
                Point pt3 = new Point(midX + deltaX, midY + deltaY);
                il.Images.Add(SORT_INDICATOR_UP_KEY, this.MakeTriangleBitmap(il.ImageSize, new Point[] { pt1, pt2, pt3 }));
            }

            if (il.Images.IndexOfKey(SORT_INDICATOR_DOWN_KEY) == -1) {
                Point pt1 = new Point(midX - deltaX, midY - deltaY);
                Point pt2 = new Point(midX, midY + deltaY);
                Point pt3 = new Point(midX + deltaX, midY - deltaY);
                il.Images.Add(SORT_INDICATOR_DOWN_KEY, this.MakeTriangleBitmap(il.ImageSize, new Point[] { pt1, pt2, pt3 }));
            }

            this.SmallImageList = il;
        }

        private Bitmap MakeTriangleBitmap(Size sz, Point[] pts) {
            Bitmap bm = new Bitmap(sz.Width, sz.Height);
            Graphics g = Graphics.FromImage(bm);
            g.FillPolygon(new SolidBrush(Color.Gray), pts);
            return bm;
        }

        /// <summary>
        /// Remove any sorting and revert to the given order of the model objects
        /// </summary>
        public virtual void Unsort() {
            this.ShowGroups = false;
            this.PrimarySortColumn = null;
            this.PrimarySortOrder = SortOrder.None;
            this.BuildList();
        }

        #endregion

        #region Utilities

        /// <summary>
        /// Do the actual work of creating the given list of groups
        /// </summary>
        /// <param name="groups"></param>
        protected virtual void CreateGroups(IList<OLVGroup> groups) {
            this.Groups.Clear();
            // The group must be added before it is given items, otherwise an exception is thrown (is this documented?)
            foreach (OLVGroup group in groups) {
                group.InsertGroupOldStyle(this);
                group.SetItemsOldStyle();
            }
            this.ResizeLastGroup();
        }

        internal void ResizeLastGroup()
        {
            // Don't mess with the control in design mode
            if (this.IsDesignMode) 
                return;

            // Sanity checks
            if (this.GetItemCount() == 0 || !this.ShowGroups || this.Groups.Count == 0) 
                return;
            
            // Get the last group and make sure we know which OLVGroup it came from
            ListViewGroup grp = this.Groups[this.Groups.Count-1];
            OLVGroup olvGroup = grp.Tag as OLVGroup;
            if (olvGroup == null)
                return;

            int height = this.GetItem(0).Bounds.Height;

            // Is the Horizontal scrollbar visible
            if (NativeMethods.HasHorizontalScrollBar(this))
                height += SystemInformation.HorizontalScrollBarHeight;
            
            if (this.SpaceBetweenGroups > height) 
                return;
            
            NativeMethods.LVGROUPMETRICS metrics = new NativeMethods.LVGROUPMETRICS();
            metrics.cbSize = ((uint)Marshal.SizeOf(typeof(NativeMethods.LVGROUPMETRICS)));
            metrics.mask = (uint)GroupMetricsMask.LVGMF_BORDERSIZE;
            metrics.Bottom = (uint)height;
            NativeMethods.SetGroupMetrics(this, olvGroup.GroupId, metrics);
        }

        /// <summary>
        /// For some reason, UseItemStyleForSubItems doesn't work for the colors
        /// when owner drawing the list, so we have to specifically give each subitem
        /// the desired colors
        /// </summary>
        /// <param name="olvi">The item whose subitems are to be corrected</param>
        /// <remarks>Cells drawn via BaseRenderer don't need this, but it is needed
        /// when an owner drawn cell uses DrawDefault=true</remarks>
        protected virtual void CorrectSubItemColors(ListViewItem olvi) {
        }

        /// <summary>
        /// Fill in the given OLVListItem with values of the given row
        /// </summary>
        /// <param name="lvi">the OLVListItem that is to be stuff with values</param>
        /// <param name="rowObject">the model object from which values will be taken</param>
        protected virtual void FillInValues(OLVListItem lvi, object rowObject) {
            if (this.Columns.Count == 0)
                return;

            OLVListSubItem subItem = this.MakeSubItem(rowObject, this.GetColumn(0));
            lvi.SubItems[0] = subItem;
            lvi.ImageSelector = subItem.ImageSelector;

            // Only Details and Tile views have subitems
            if (this.View == View.Details) {
                for (int i = 1; i < this.Columns.Count; i++) {
                    lvi.SubItems.Add(this.MakeSubItem(rowObject, this.GetColumn(i)));
                }
            } else {
                if (this.View == View.Tile) {
                    for (int i = 1; i < this.Columns.Count; i++) {
                        OLVColumn column = this.GetColumn(i);
                        if (column.IsTileViewColumn)
                            lvi.SubItems.Add(this.MakeSubItem(rowObject, column));
                    }
                }
            }

            // Give the item the same font/colors as the control
            lvi.Font = this.Font;
            lvi.BackColor = this.BackColor;
            lvi.ForeColor = this.ForeColor;

            // Set the check state of the row, if we are showing check boxes
            if (this.CheckBoxes) {
                CheckState? state = this.GetCheckState(lvi.RowObject);
                if (state.HasValue)
                    lvi.CheckState = (CheckState)state;
            }

            // Give the RowFormatter a chance to mess with the item
            if (this.RowFormatter != null) {
                this.RowFormatter(lvi);
            }
        }

        private OLVListSubItem MakeSubItem(object rowObject, OLVColumn column) {
            object cellValue = column.GetValue(rowObject);
            OLVListSubItem subItem = new OLVListSubItem(cellValue,
                                                        column.ValueToString(cellValue),
                                                        column.GetImage(rowObject));
            if (this.UseHyperlinks && column.Hyperlink) {
                IsHyperlinkEventArgs args = new IsHyperlinkEventArgs();
                args.ListView = this;
                args.Model = rowObject;
                args.Column = column;
                args.Text = subItem.Text;
                args.Url = subItem.Text;
                this.OnIsHyperlink(args);
                subItem.Url = args.Url;
            }

            return subItem;
        }

        private void ApplyHyperlinkStyle(int rowIndex, OLVListItem olvi) {
            olvi.UseItemStyleForSubItems = false;

            // If subitem 0 is given a back color, the item back color is changed too.
            // So we have to remember it here so we can used it even if subitem 0 is changed.
            Color itemBackColor = olvi.BackColor;

            for (int i = 0; i < this.Columns.Count; i++) {
                OLVListSubItem subItem = olvi.GetSubItem(i);
                if (subItem == null)
                    continue;
                OLVColumn column = this.GetColumn(i);
                subItem.BackColor = itemBackColor;
                if (column.Hyperlink && !String.IsNullOrEmpty(subItem.Url)) {
                    if (this.IsUrlVisited(subItem.Url))
                        this.ApplyCellStyle(olvi, i, this.HyperlinkStyle.Visited);
                    else
                        this.ApplyCellStyle(olvi, i, this.HyperlinkStyle.Normal);
                }
            }
        }


        /// <summary>
        /// Make sure the ListView has the extended style that says to display subitem images.
        /// </summary>
        /// <remarks>This method must be called after any .NET call that update the extended styles
        /// since they seem to erase this setting.</remarks>
        protected virtual void ForceSubItemImagesExStyle() {
            // Virtual lists can't show subitem images natively, so don't turn on this flag
            if (!this.VirtualMode)
                NativeMethods.ForceSubItemImagesExStyle(this);
        }

        /// <summary>
        /// Convert the given image selector to an index into our image list.
        /// Return -1 if that's not possible
        /// </summary>
        /// <param name="imageSelector"></param>
        /// <returns>Index of the image in the imageList, or -1</returns>
        protected virtual int GetActualImageIndex(Object imageSelector) {
            if (imageSelector == null)
                return -1;

            if (imageSelector is Int32)
                return (int)imageSelector;

            String imageSelectorAsString = imageSelector as String;
            if (imageSelectorAsString != null && this.SmallImageList != null)
                return this.SmallImageList.Images.IndexOfKey(imageSelectorAsString);

            return -1;
        }

        /// <summary>
        /// Return the tooltip that should be shown when the mouse is hovered over the given column
        /// </summary>
        /// <param name="columnIndex">The column index whose tool tip is to be fetched</param>
        /// <returns>A string or null if no tool tip is to be shown</returns>
        public virtual String GetHeaderToolTip(int columnIndex) {
            OLVColumn column = this.GetColumn(columnIndex);
            if (column == null)
                return null;
            String tooltip = column.ToolTipText;
            if (this.HeaderToolTipGetter != null)
                tooltip = this.HeaderToolTipGetter(column);
            return tooltip;
        }

        /// <summary>
        /// Return the tooltip that should be shown when the mouse is hovered over the given cell
        /// </summary>
        /// <param name="columnIndex">The column index whose tool tip is to be fetched</param>
        /// <param name="rowIndex">The row index whose tool tip is to be fetched</param>
        /// <returns>A string or null if no tool tip is to be shown</returns>
        public virtual String GetCellToolTip(int columnIndex, int rowIndex) {
            if (this.CellToolTipGetter != null)
                return this.CellToolTipGetter(this.GetColumn(columnIndex), this.GetModelObject(rowIndex));

            // Show the URL in the tooltip if it's different to the text
            if (columnIndex >= 0) {
                OLVListSubItem subItem = this.GetSubItem(rowIndex, columnIndex);
                if (subItem != null && !String.IsNullOrEmpty(subItem.Url) && subItem.Url != subItem.Text &&
                    this.HotCellHitLocation == HitTestLocation.Text)
                    return subItem.Url;
            }

            return null;
        }

        /// <summary>
        /// Return the OLVListItem that displays the given model object
        /// </summary>
        /// <param name="modelObject">The modelObject whose item is to be found</param>
        /// <returns>The OLVListItem that displays the model, or null</returns>
        /// <remarks>This method has O(n) performance.</remarks>
        public virtual OLVListItem ModelToItem(object modelObject) {
            if (modelObject == null)
                return null;

            foreach (OLVListItem olvi in this.Items) {
                if (olvi.RowObject != null && olvi.RowObject.Equals(modelObject))
                    return olvi;
            }
            return null;
        }

        /// <summary>
        /// Do the work required after the items in a listview have been created
        /// </summary>
        protected virtual void PostProcessRows() {
            // If this method is called during a BeginUpdate/EndUpdate pair, changes to the
            // Items collection are cached. Getting the Count flushes that cache.
            int count = this.Items.Count;

            int i = 0;
            if (this.ShowGroups) {
                foreach (ListViewGroup group in this.Groups) {
                    foreach (OLVListItem olvi in group.Items) {
                        this.PostProcessOneRow(olvi.Index, i, olvi);
                        i++;
                    }
                }
            } else {
                foreach (OLVListItem olvi in this.Items) {
                    this.PostProcessOneRow(olvi.Index, i, olvi);
                    i++;
                }
            }
        }

        /// <summary>
        /// Do the work required after one item in a listview have been created
        /// </summary>
        protected virtual void PostProcessOneRow(int rowIndex, int displayIndex, OLVListItem olvi) {
            if (this.UseAlternatingBackColors && this.View == View.Details) {
                if (displayIndex % 2 == 1) {
                    olvi.BackColor = this.AlternateRowBackColorOrDefault;
                } else {
                    olvi.BackColor = this.BackColor;
                }
            }
            if (this.ShowImagesOnSubItems && !this.VirtualMode) {
                this.SetSubItemImages(rowIndex, olvi);
            }
            if (this.UseHyperlinks) {
                this.ApplyHyperlinkStyle(rowIndex, olvi);
            }
            this.TriggerFormatRowEvent(rowIndex, displayIndex, olvi);
        }

        /// <summary>
        /// Prepare the listview to show alternate row backcolors
        /// </summary>
        /// <remarks>We cannot rely on lvi.Index in this method.
        /// In a straight list, lvi.Index is the display index, and can be used to determine
        /// whether the row should be colored. But when organised by groups, lvi.Index is not
        /// useable because it still refers to the position in the overall list, not the display order.
        ///</remarks>
        [Obsolete("This method is no longer used. Override PostProcessOneRow() to achieve a similar result")]
        protected virtual void PrepareAlternateBackColors() {
        }

        /// <summary>
        /// Setup all subitem images on all rows
        /// </summary>
        [Obsolete("This method is not longer maintained and will be removed", false)]
        protected virtual void SetAllSubItemImages() {
            //if (!this.ShowImagesOnSubItems || this.OwnerDraw)
            //    return;

            //this.ForceSubItemImagesExStyle();

            //for (int rowIndex = 0; rowIndex < this.GetItemCount(); rowIndex++)
            //    SetSubItemImages(rowIndex, this.GetItem(rowIndex));
        }

        /// <summary>
        /// Tell the underlying list control which images to show against the subitems
        /// </summary>
        /// <param name="rowIndex">the index at which the item occurs</param>
        /// <param name="item">the item whose subitems are to be set</param>
        protected virtual void SetSubItemImages(int rowIndex, OLVListItem item) {
            this.SetSubItemImages(rowIndex, item, false);
        }

        /// <summary>
        /// Tell the underlying list control which images to show against the subitems
        /// </summary>
        /// <param name="rowIndex">the index at which the item occurs</param>
        /// <param name="item">the item whose subitems are to be set</param>
        /// <param name="shouldClearImages">will existing images be cleared if no new image is provided?</param>
        protected virtual void SetSubItemImages(int rowIndex, OLVListItem item, bool shouldClearImages) {
            if (!this.ShowImagesOnSubItems || this.OwnerDraw)
                return;

            for (int i = 1; i < item.SubItems.Count; i++) {
                this.SetSubItemImage(rowIndex, i, item.GetSubItem(i), shouldClearImages);
            }
        }

        /// <summary>
        /// Set the subitem image natively
        /// </summary>
        /// <param name="rowIndex"></param>
        /// <param name="subItemIndex"></param>
        /// <param name="subItem"></param>
        /// <param name="shouldClearImages"></param>
        public virtual void SetSubItemImage(int rowIndex, int subItemIndex, OLVListSubItem subItem, bool shouldClearImages) {
            int imageIndex = this.GetActualImageIndex(subItem.ImageSelector);
            if (shouldClearImages || imageIndex != -1)
                NativeMethods.SetSubItemImage(this, rowIndex, subItemIndex, imageIndex);
        }

        /// <summary>
        /// Take ownership of the 'objects' collection. This separats our collection from the source.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This method
        /// separates the 'objects' instance variable from its source, so that any AddObject/RemoveObject
        /// calls will modify our collection and not the original colleciton.
        /// </para>
        /// <para>
        /// This method has the intentional side-effect of converting our list of objects to an ArrayList.
        /// </para>
        /// </remarks>
        protected virtual void TakeOwnershipOfObjects() {
            if (this.isOwnerOfObjects)
                return;

            this.isOwnerOfObjects = true;

            this.objects = ObjectListView.EnumerableToArray(this.objects, true);
        }

        /// <summary>
        /// Trigger FormatRow and possibly FormatCell events for the given item
        /// </summary>
        /// <param name="rowIndex"></param>
        /// <param name="displayIndex"></param>
        /// <param name="olvi"></param>
        protected virtual void TriggerFormatRowEvent(int rowIndex, int displayIndex, OLVListItem olvi) {
            FormatRowEventArgs args = new FormatRowEventArgs();
            args.ListView = this;
            args.RowIndex = rowIndex;
            args.DisplayIndex = displayIndex;
            args.Item = olvi;
            args.UseCellFormatEvents = this.UseCellFormatEvents;
            this.OnFormatRow(args);

            if (args.UseCellFormatEvents && this.View == View.Details) {
                // If a cell isn't given its own color, it should use the color of the item.
                // However, there is a bug in the .NET framework where the cell are given
                // the color of the ListView instead. So we have to explicitly give each
                // cell the back color that it should have.
                olvi.UseItemStyleForSubItems = false;
                Color backColor = olvi.BackColor;
                for (int i = 0; i < this.Columns.Count; i++) {
                    olvi.SubItems[i].BackColor = backColor;
                }

                // Fire one event per cell
                FormatCellEventArgs args2 = new FormatCellEventArgs();
                args2.ListView = this;
                args2.RowIndex = rowIndex;
                args2.DisplayIndex = displayIndex;
                args2.Item = olvi;
                for (int i = 0; i < this.Columns.Count; i++) {
                    args2.ColumnIndex = i;
                    args2.Column = this.GetColumn(i);
                    args2.SubItem = olvi.GetSubItem(i);
                    this.OnFormatCell(args2);
                }
            }
        }

        #endregion

        #region ISupportInitialize Members

        void ISupportInitialize.BeginInit() {
            this.Frozen = true;
        }

        void ISupportInitialize.EndInit() {
            if (this.RowHeight != -1) {
                this.SmallImageList = this.SmallImageList;
                if (this.CheckBoxes)
                    this.InitializeStateImageList();
            }

            if (this.UseCustomSelectionColors)
                this.EnableCustomSelectionColors();

            if (this.UseSubItemCheckBoxes || (this.VirtualMode && this.CheckBoxes))
                this.SetupSubItemCheckBoxes();

            this.Frozen = false;
        }

        #endregion

        #region Image list manipulation

        /// <summary>
        /// Update our externally visible image list so it holds the same images as our shadow list, but sized correctly
        /// </summary>
        private void SetupBaseImageList() {
            // If a row height hasn't been set, or an image list has been give which is the required size, just assign it
            if (rowHeight == -1 ||
                this.View != View.Details ||
                (this.shadowedImageList != null && this.shadowedImageList.ImageSize.Height == rowHeight))
                this.BaseSmallImageList = this.shadowedImageList;
            else {
                int width = (this.shadowedImageList == null ? 16 : this.shadowedImageList.ImageSize.Width);
                this.BaseSmallImageList = this.MakeResizedImageList(width, rowHeight, shadowedImageList);
            }
        }

        /// <summary>
        /// Return a copy of the given source image list, where each image has been resized to be height x height in size.
        /// If source is null, an empty image list of the given size is returned
        /// </summary>
        /// <param name="width">Height and width of the new images</param>
        /// <param name="height">Height and width of the new images</param>
        /// <param name="source">Source of the images (can be null)</param>
        /// <returns>A new image list</returns>
        private ImageList MakeResizedImageList(int width, int height, ImageList source) {
            ImageList il = new ImageList();
            il.ImageSize = new Size(width, height);
            il.ColorDepth = ColorDepth.Depth32Bit;

            // If there's nothing to copy, just return the new list
            if (source == null)
                return il;

            il.TransparentColor = source.TransparentColor;
            il.ColorDepth = source.ColorDepth;

            // Fill the imagelist with resized copies from the source
            for (int i = 0; i < source.Images.Count; i++) {
                Bitmap bm = this.MakeResizedImage(width, height, source.Images[i], source.TransparentColor);
                il.Images.Add(bm);
            }

            // Give each image the same key it has in the original
            foreach (String key in source.Images.Keys) {
                il.Images.SetKeyName(source.Images.IndexOfKey(key), key);
            }

            return il;
        }

        /// <summary>
        /// Return a bitmap of the given height x height, which shows the given image, centred.
        /// </summary>
        /// <param name="width">Height and width of new bitmap</param>
        /// <param name="height">Height and width of new bitmap</param>
        /// <param name="image">Image to be centred</param>
        /// <param name="transparent">The background color</param>
        /// <returns>A new bitmap</returns>
        private Bitmap MakeResizedImage(int width, int height, Image image, Color transparent) {
            Bitmap bm = new Bitmap(width, height);
            Graphics g = Graphics.FromImage(bm);
            g.Clear(transparent);
            int x = Math.Max(0, (bm.Size.Width - image.Size.Width) / 2);
            int y = Math.Max(0, (bm.Size.Height - image.Size.Height) / 2);
            g.DrawImage(image, x, y, image.Size.Width, image.Size.Height);
            return bm;
        }

        /// <summary>
        /// Initialize the state image list with the required checkbox images
        /// </summary>
        protected virtual void InitializeStateImageList() {
            if (this.DesignMode)
                return;

            if (this.StateImageList == null) {
                this.StateImageList = new ImageList();
                this.StateImageList.ImageSize = new Size(16, 16);
                this.StateImageList.ColorDepth = ColorDepth.Depth32Bit;
            }

            if (this.RowHeight != -1 &&
                this.View == View.Details &&
                this.StateImageList.ImageSize.Height != this.RowHeight) {
                this.StateImageList = new ImageList();
                this.StateImageList.ImageSize = new Size(16, this.RowHeight);
                this.StateImageList.ColorDepth = ColorDepth.Depth32Bit;
            }

            if (!this.CheckBoxes)
                return;

            // The internal logic of ListView cycles through the state images when the primary
            // checkbox is clicked. So we have to get exactly the right number of images in the 
            // image list.
            if (this.StateImageList.Images.Count == 0)
                this.AddCheckStateBitmap(this.StateImageList, UNCHECKED_KEY, CheckBoxState.UncheckedNormal);
            if (this.StateImageList.Images.Count <= 1)
                this.AddCheckStateBitmap(this.StateImageList, CHECKED_KEY, CheckBoxState.CheckedNormal);
            if (this.TriStateCheckBoxes && this.StateImageList.Images.Count <= 2)
                this.AddCheckStateBitmap(this.StateImageList, INDETERMINATE_KEY, CheckBoxState.MixedNormal);
            else {
                if (this.StateImageList.Images.ContainsKey(INDETERMINATE_KEY))
                    this.StateImageList.Images.RemoveByKey(INDETERMINATE_KEY);
            }
        }

        /// <summary>
        /// The name of the image used when a check box is checked
        /// </summary>
        public const string CHECKED_KEY = "checkbox-checked";

        /// <summary>
        /// The name of the image used when a check box is unchecked
        /// </summary>
        public const string UNCHECKED_KEY = "checkbox-unchecked";

        /// <summary>
        /// The name of the image used when a check box is Indeterminate
        /// </summary>
        public const string INDETERMINATE_KEY = "checkbox-indeterminate";

        /// <summary>
        /// Setup this control so it can display check boxes on subitems
        /// (or primary checkboxes in virtual mode)
        /// </summary>
        /// <remarks>This gives the ListView a small image list, if it doesn't already have one.</remarks>
        public virtual void SetupSubItemCheckBoxes() {
            this.ShowImagesOnSubItems = true;
            if (this.SmallImageList == null || !this.SmallImageList.Images.ContainsKey(CHECKED_KEY))
                this.InitializeCheckBoxImages();
        }

        /// <summary>
        /// Make sure the small image list for this control has checkbox images 
        /// (used for sub-item checkboxes).
        /// </summary>
        /// <remarks>This gives the ListView a small image list, if it doesn't already have one.</remarks>
        protected virtual void InitializeCheckBoxImages() {
            // Don't mess with the image list in design mode
            if (this.DesignMode)
                return;

            ImageList il = this.SmallImageList;
            if (il == null) {
                il = new ImageList();
                il.ImageSize = new Size(16, 16);
                il.ColorDepth = ColorDepth.Depth32Bit;
            }

            this.AddCheckStateBitmap(il, CHECKED_KEY, CheckBoxState.CheckedNormal);
            this.AddCheckStateBitmap(il, UNCHECKED_KEY, CheckBoxState.UncheckedNormal);
            this.AddCheckStateBitmap(il, INDETERMINATE_KEY, CheckBoxState.MixedNormal);

            this.SmallImageList = il;
        }

        private void AddCheckStateBitmap(ImageList il, string key, CheckBoxState boxState) {
            Bitmap b = new Bitmap(il.ImageSize.Width, il.ImageSize.Height);
            Graphics g = Graphics.FromImage(b);
            g.Clear(il.TransparentColor);
            Point location = new Point(b.Width / 2 - 5, b.Height / 2 - 6);
            CheckBoxRenderer.DrawCheckBox(g, location, boxState);
            il.Images.Add(key, b);
        }

        #endregion

        #region Owner drawing

        /// <summary>
        /// Owner draw the column header
        /// </summary>
        /// <param name="e"></param>
        protected override void OnDrawColumnHeader(DrawListViewColumnHeaderEventArgs e) {
            e.DrawDefault = true;
            base.OnDrawColumnHeader(e);
        }

        /// <summary>
        /// Owner draw the item
        /// </summary>
        /// <param name="e"></param>
        protected override void OnDrawItem(DrawListViewItemEventArgs e) {
            if (this.View == View.Details)
                e.DrawDefault = false;
            else {
                if (this.ItemRenderer == null)
                    e.DrawDefault = true;
                else {
                    Object row = ((OLVListItem)e.Item).RowObject;
                    e.DrawDefault = !this.ItemRenderer.RenderItem(e, e.Graphics, e.Bounds, row);
                }
            }

            if (e.DrawDefault)
                base.OnDrawItem(e);
        }

        /// <summary>
        /// Owner draw a single subitem
        /// </summary>
        /// <param name="e"></param>
        protected override void OnDrawSubItem(DrawListViewSubItemEventArgs e) {
            //System.Diagnostics.Debug.WriteLine(String.Format("OnDrawSubItem ({0}, {1})", e.ItemIndex, e.ColumnIndex));
            // Don't try to do owner drawing at design time
            if (this.DesignMode) {
                e.DrawDefault = true;
                return;
            }

            // Calculate where the subitem should be drawn
            Rectangle r = e.Bounds;

            // Optimize drawing by only redrawing subitems that touch the area that was damaged
            //if (!r.IntersectsWith(this.lastUpdateRectangle))
            //    return;

            // Get the special renderer for this column. If there isn't one, use the default draw mechanism.
            OLVColumn column = this.GetColumn(e.ColumnIndex);
            IRenderer renderer = column.Renderer ?? this.DefaultRenderer;

            // Get a graphics context for the renderer to use.
            // But we have more complications. Virtual lists have a nasty habit of drawing column 0
            // whenever there is any mouse move events over a row, and doing it in an un-double-buffered manner,
            // which results in nasty flickers! There are also some unbuffered draw when a mouse is first
            // hovered over column 0 of a normal row. So, to avoid all complications,
            // we always manually double-buffer the drawing.
            // Except with Mono, which doesn't seem to handle double buffering at all :-(
            Graphics g = e.Graphics;
            BufferedGraphics buffer = null;
            bool avoidFlickerMode = true; // set this to false to see the problems with flicker
            if (avoidFlickerMode) {
                buffer = BufferedGraphicsManager.Current.Allocate(e.Graphics, r);
                g = buffer.Graphics;
            }

            g.TextRenderingHint = ObjectListView.TextRenderingHint;
            g.SmoothingMode = ObjectListView.SmoothingMode;

            // Finally, give the renderer a chance to draw something
            e.DrawDefault = !renderer.RenderSubItem(e, g, r, ((OLVListItem)e.Item).RowObject);

            if (buffer != null) {
                if (!e.DrawDefault)
                    buffer.Render();
                buffer.Dispose();
            }
        }

        #endregion

        #region OnEvent Handling

        /// <summary>
        /// We need the click count in the mouse up event, but that is always 1.
        /// So we have to remember the click count from the preceding mouse down event.
        /// </summary>
        /// <param name="e"></param>
        protected override void OnMouseDown(MouseEventArgs e) {
            this.lastMouseDownClickCount = e.Clicks;
            base.OnMouseDown(e);
        }
        private int lastMouseDownClickCount;

        /// <summary>
        /// When the mouse leaves the control, remove any hot item highlighting
        /// </summary>
        /// <param name="e"></param>
        protected override void OnMouseLeave(EventArgs e) {
            base.OnMouseLeave(e);

            if (!this.Created)
                return;

            this.UpdateHotItem(new Point(-1, -1));
        }

        // We could change the hot item on the mouse hover event, but it looks wrong.

        //protected override void OnMouseHover(EventArgs e)
        //{
        //    base.OnMouseHover(e);
        //    this.UpdateHotItem(this.PointToClient(Cursor.Position));
        //}

        /// <summary>
        /// When the mouse moves, we might need to change the hot item.
        /// </summary>
        /// <param name="e"></param>
        protected override void OnMouseMove(MouseEventArgs e) {
            base.OnMouseMove(e);

            if (!this.Created)
                return;

            CellOverEventArgs args = new CellOverEventArgs();
            this.BuildCellEvent(args, e.Location);
            this.OnCellOver(args);
            this.MouseMoveHitTest = args.HitTest;

            if (!args.Handled) {
                this.UpdateHotItem(args.HitTest);
            }
        }

        /// <summary>
        /// Check to see if we need to start editing a cell
        /// </summary>
        /// <param name="e"></param>
        protected override void OnMouseUp(MouseEventArgs e) {
            base.OnMouseUp(e);

            if (!this.Created)
                return;

            if (e.Button == MouseButtons.Right) {
                this.OnRightMouseUp(e);
                return;
            }

            // What event should we listen for to start cell editing?
            // ------------------------------------------------------
            //
            // We can't use OnMouseClick, OnMouseDoubleClick, OnClick, or OnDoubleClick
            // since they are not triggered for clicks on subitems without Full Row Select.
            //
            // We could use OnMouseDown, but selecting rows is done in OnMouseUp. This means
            // that if we start the editing during OnMouseDown, the editor will automatically
            // lose focus when mouse up happens.
            //

            // Tell the world about a cell click. If someone handles it, don't do anything else
            CellClickEventArgs args = new CellClickEventArgs();
            this.BuildCellEvent(args, e.Location);
            args.ClickCount = this.lastMouseDownClickCount;
            this.OnCellClick(args);
            if (args.Handled)
                return;

            // Did the user click a hyperlink?
            if (this.UseHyperlinks &&
                args.HitTest.HitTestLocation == HitTestLocation.Text &&
                args.SubItem != null &&
                !String.IsNullOrEmpty(args.SubItem.Url)) {
                // We have to delay the running of this process otherwise we can generate
                // a series of MouseUp events (don't ask me why)
                this.BeginInvoke((MethodInvoker)delegate { ProcessHyperlinkClicked(args); });
            }

            // No one handled it so check to see if we should start editing.
            // We only start the edit if the user clicked on the image or text.
            if (this.ShouldStartCellEdit(e) &&
                args.HitTest.HitTestLocation != HitTestLocation.Nothing) {

                // We don't edit the primary column by single clicks -- only subitems.
                if (this.CellEditActivation != CellEditActivateMode.SingleClick || args.ColumnIndex > 0)
                    this.EditSubItem(args.Item, args.ColumnIndex);
            }
        }

        /// <summary>
        /// Tell the world that a hyperlink was clicked and if the event isn't handled,
        /// do the default processing.
        /// </summary>
        /// <param name="e"></param>
        protected virtual void ProcessHyperlinkClicked(CellClickEventArgs e) {
            HyperlinkClickedEventArgs args = new HyperlinkClickedEventArgs();
            args.HitTest = e.HitTest;
            args.ListView = this;
            args.Location = new Point(-1, -1);
            args.Item = e.Item;
            args.SubItem = e.SubItem;
            args.Model = e.Model;
            args.ColumnIndex = e.ColumnIndex;
            args.Column = e.Column;
            args.RowIndex = e.RowIndex;
            args.ModifierKeys = Control.ModifierKeys;
            args.Url = e.SubItem.Url;
            this.OnHyperlinkClicked(args);
            if (!args.Handled) {
                this.StandardHyperlinkClickedProcessing(args);
            }
        }

        /// <summary>
        /// Do the default processing for a hyperlink clicked event, which
        /// is to try and open the url.
        /// </summary>
        /// <param name="args"></param>
        protected virtual void StandardHyperlinkClickedProcessing(HyperlinkClickedEventArgs args) {
            Cursor originalCursor = this.Cursor;
            try {
                this.Cursor = Cursors.WaitCursor;
                System.Diagnostics.Process.Start(args.Url);
            } catch (Win32Exception) {
                System.Media.SystemSounds.Beep.Play();
                // ignore it
            } finally {
                this.Cursor = originalCursor;
            }
            this.MarkUrlVisited(args.Url);
            this.RefreshHotItem();
        }

        /// <summary>
        /// The user right clicked on the control
        /// </summary>
        /// <param name="e"></param>
        protected virtual void OnRightMouseUp(MouseEventArgs e) {
            CellRightClickEventArgs args = new CellRightClickEventArgs();
            this.BuildCellEvent(args, e.Location);
            this.OnCellRightClick(args);
            if (!args.Handled) {
                if (args.MenuStrip != null) {
                    args.MenuStrip.Show(this, args.Location);
                }
            }
        }

        private void BuildCellEvent(CellEventArgs args, Point location) {
            OlvListViewHitTestInfo hitTest = this.OlvHitTest(location.X, location.Y);
            args.HitTest = hitTest;
            args.ListView = this;
            args.Location = location;
            args.Item = hitTest.Item;
            args.SubItem = hitTest.SubItem;
            args.Model = hitTest.RowObject;
            args.ColumnIndex = hitTest.ColumnIndex;
            args.Column = hitTest.Column;
            if (hitTest.Item != null)
                args.RowIndex = hitTest.Item.Index;
            args.ModifierKeys = Control.ModifierKeys;

            // In non-details view, we want any hit on an item to act as if it was a hit
            // on column 0 -- which, effectively, it was.
            if (args.Item != null && args.ListView.View != View.Details) {
                args.ColumnIndex = 0;
                args.Column = args.ListView.GetColumn(0);
                args.SubItem = args.Item.GetSubItem(0);
            }
        }

        /// <summary>
        /// This method is called every time a row is selected or deselected. This can be
        /// a pain if the user shift-clicks 100 rows. We override this method so we can
        /// trigger one event for any number of select/deselects that come from one user action
        /// </summary>
        /// <param name="e"></param>
        protected override void OnSelectedIndexChanged(EventArgs e) {
            base.OnSelectedIndexChanged(e);

            // If we haven't already scheduled an event, schedule it to be triggered
            // By using idle time, we will wait until all select events for the same
            // user action have finished before triggering the event.
            if (!this.hasIdleHandler) {
                this.hasIdleHandler = true;
                Application.Idle += new EventHandler(HandleApplicationIdle);
            }
        }

        /// <summary>
        /// Called when the handle of the underlying control is created
        /// </summary>
        /// <param name="e"></param>
        protected override void OnHandleCreated(EventArgs e) {
            base.OnHandleCreated(e);

            this.Invoke((MethodInvoker)this.OnControlCreated);
        }

        /// <summary>
        /// This method is called after the control has been fully created.
        /// </summary>
        protected virtual void OnControlCreated() {

            // Force the header control to be created when the listview handle is
            HeaderControl hc = this.HeaderControl;
            hc.WordWrap = this.HeaderWordWrap;

            // Make sure any overlays that are set on the hot item style take effect
            this.HotItemStyle = this.HotItemStyle;

            // Arrange for any group images to be installed after the control is created
            int x = NativeMethods.SetGroupImageList(this, this.GroupImageList);

            this.UseExplorerTheme = this.UseExplorerTheme;

            this.RememberDisplayIndicies();
        }

        #endregion

        #region Cell editing

        /// <summary>
        /// Should we start editing the cell in response to the given mouse button event?
        /// </summary>
        /// <param name="e"></param>
        /// <returns></returns>
        protected virtual bool ShouldStartCellEdit(MouseEventArgs e) {
            if (this.IsCellEditing)
                return false;

            if (e.Button != MouseButtons.Left)
                return false;

            if ((Control.ModifierKeys & (Keys.Shift | Keys.Control | Keys.Alt)) != 0)
                return false;

            if (this.lastMouseDownClickCount == 1 && this.CellEditActivation == CellEditActivateMode.SingleClick)
                return true;

            return (this.lastMouseDownClickCount == 2 && this.CellEditActivation == CellEditActivateMode.DoubleClick);
        }

        /// <summary>
        /// Handle a key press on this control. We specifically look for F2 which edits the primary column,
        /// or a Tab character during an edit operation, which tries to start editing on the next (or previous) cell.
        /// </summary>
        /// <param name="keyData"></param>
        /// <returns></returns>
        protected override bool ProcessDialogKey(Keys keyData) {

            if (this.IsCellEditing) 
                return this.CellEditKeyEngine.HandleKey(this, keyData);

            // Treat F2 as a request to edit the primary column
            if (keyData == Keys.F2) {
                this.EditSubItem((OLVListItem)this.FocusedItem, 0);
                return base.ProcessDialogKey(keyData);
            }

            // Treat Ctrl-C as Copy To Clipboard. 
            if (this.CopySelectionOnControlC && keyData == (Keys.C | Keys.Control)) {
                this.CopySelectionToClipboard();
                return true;
            }

            // Treat Ctrl-A as Select All.
            if (this.SelectAllOnControlA && keyData == (Keys.A | Keys.Control)) {
                this.SelectAll();
                return true;
            }

            return base.ProcessDialogKey(keyData);
        }
       
        /// <summary>
        /// Begin an edit operation on the given cell.
        /// </summary>
        /// <remarks>This performs various sanity checks and passes off the real work to StartCellEdit().</remarks>
        /// <param name="item">The row to be edited</param>
        /// <param name="subItemIndex">The index of the cell to be edited</param>
        public virtual void EditSubItem(OLVListItem item, int subItemIndex) {
            if (item == null)
                return;

            if (subItemIndex < 0 && subItemIndex >= item.SubItems.Count)
                return;

            if (this.CellEditActivation == CellEditActivateMode.None)
                return;

            if (!this.GetColumn(subItemIndex).IsEditable)
                return;

            this.StartCellEdit(item, subItemIndex);
        }

        /// <summary>
        /// Really start an edit operation on a given cell. The parameters are assumed to be sane.
        /// </summary>
        /// <param name="item">The row to be edited</param>
        /// <param name="subItemIndex">The index of the cell to be edited</param>
        public virtual void StartCellEdit(OLVListItem item, int subItemIndex) {
            OLVColumn column = this.GetColumn(subItemIndex);
            Rectangle r = this.CalculateCellEditorBounds(item, subItemIndex);
            Control c = this.GetCellEditor(item, subItemIndex);
            c.Bounds = r;

            // Try to align the control as the column is aligned. Not all controls support this property
            Munger.PutProperty(c, "TextAlign", column.TextAlign);

            // Give the control the value from the model
            this.SetControlValue(c, column.GetValue(item.RowObject), column.GetStringValue(item.RowObject));

            // Give the outside world the chance to munge with the process
            this.cellEditEventArgs = new CellEditEventArgs(column, c, r, item, subItemIndex);
            this.OnCellEditStarting(this.cellEditEventArgs);
            if (this.cellEditEventArgs.Cancel)
                return;

            // The event handler may have completely changed the control, so we need to remember it
            this.cellEditor = this.cellEditEventArgs.Control;

            // If the control isn't the height of the cell, centre it vertically. We don't
            // need to do this when in Tile view.
            if (this.View != View.Tile && this.cellEditor.Height != r.Height)
                this.cellEditor.Top += (r.Height - this.cellEditor.Height) / 2;

            this.Invalidate();
            this.Controls.Add(this.cellEditor);
            this.ConfigureControl();
            this.PauseAnimations(true);
        }
        private Control cellEditor;
        internal CellEditEventArgs cellEditEventArgs;

        /// <summary>
        /// Calculate the bounds of the edit control for the given item/column
        /// </summary>
        /// <param name="item"></param>
        /// <param name="subItemIndex"></param>
        /// <returns></returns>
        public Rectangle CalculateCellEditorBounds(OLVListItem item, int subItemIndex) {
            Rectangle r;
            if (this.View == View.Details)
                r = item.GetSubItemBounds(subItemIndex);
            else
                r = this.GetItemRect(item.Index, ItemBoundsPortion.Label);
            if (this.OwnerDraw)
                return CalculateCellEditorBoundsOwnerDrawn(item, subItemIndex, r);
            else
                return CalculateCellEditorBoundsStandard(item, subItemIndex, r);
        }

        /// <summary>
        /// Calculate the bounds of the edit control for the given item/column, when the listview
        /// is being owner drawn.
        /// </summary>
        /// <param name="item"></param>
        /// <param name="subItemIndex"></param>
        /// <param name="r"></param>
        /// <returns>A rectangle that is the bounds of the cell editor</returns>
        protected Rectangle CalculateCellEditorBoundsOwnerDrawn(OLVListItem item, int subItemIndex, Rectangle r) {
            IRenderer renderer = null;
            if (this.View == View.Details)
                renderer = this.GetColumn(subItemIndex).Renderer ?? this.DefaultRenderer;
            else
                renderer = this.ItemRenderer;

            if (renderer == null)
                return r;
            else {
                using (Graphics g = this.CreateGraphics()) {
                    return renderer.GetEditRectangle(g, r, item, subItemIndex);
                }
            }
        }

        /// <summary>
        /// Calculate the bounds of the edit control for the given item/column, when the listview
        /// is not being owner drawn.
        /// </summary>
        /// <param name="item"></param>
        /// <param name="subItemIndex"></param>
        /// <param name="cellBounds"></param>
        /// <returns>A rectangle that is the bounds of the cell editor</returns>
        protected Rectangle CalculateCellEditorBoundsStandard(OLVListItem item, int subItemIndex, Rectangle cellBounds) {
            if (this.View != View.Details)
                return cellBounds;//

            // Allow for image (if there is one)
            int offset = 0;
            object subItemImageSelector = item.ImageSelector;
            if (subItemIndex > 0)
                subItemImageSelector = ((OLVListSubItem)item.SubItems[subItemIndex]).ImageSelector;
            if (this.GetActualImageIndex(subItemImageSelector) != -1) {
                offset += this.SmallImageSize.Width + 2;
            }

            // Allow for checkbox
            if (this.CheckBoxes && this.StateImageList != null && subItemIndex == 0) {
                offset += this.StateImageList.ImageSize.Width + 2;
            }

            // Allow for indent (first column only)
            if (subItemIndex == 0 && item.IndentCount > 0) {
                offset += (this.SmallImageSize.Width * item.IndentCount);
            }

            // Do the adjustment
            if (offset > 0) {
                cellBounds.X += offset;
                cellBounds.Width -= offset;
            }

            return cellBounds;
        }

        /// <summary>
        /// Try to give the given value to the provided control. Fall back to assigning a string
        /// if the value assignment fails.
        /// </summary>
        /// <param name="control">A control</param>
        /// <param name="value">The value to be given to the control</param>
        /// <param name="stringValue">The string to be given if the value doesn't work</param>
        protected virtual void SetControlValue(Control control, Object value, String stringValue) {
            // Handle combobox explicitly
            if (control is ComboBox) {
                ComboBox cb = ((ComboBox)control);
                if (cb.Created)
                    cb.SelectedValue = value;
                else
                    this.BeginInvoke(new MethodInvoker(delegate {
                        cb.SelectedValue = value;
                    }));
                return;
            }


            if (Munger.PutProperty(control, "Value", value))
                return;

            // There wasn't a Value property, or we couldn't set it, so set the text instead
            try {
                String valueAsString = value as String;
                if (valueAsString == null)
                    control.Text = stringValue;
                else
                    control.Text = valueAsString;
            } catch (ArgumentOutOfRangeException) {
                // The value couldn't be set via the Text property.
            }
        }

        /// <summary>
        /// Setup the given control to be a cell editor
        /// </summary>
        protected virtual void ConfigureControl() {
            this.cellEditor.Validating += new CancelEventHandler(CellEditor_Validating);
            this.cellEditor.Select();
        }

        /// <summary>
        /// Return the value that the given control is showing
        /// </summary>
        /// <param name="control"></param>
        /// <returns></returns>
        protected virtual Object GetControlValue(Control control) {
            if (control == null)
                return null;

            if (control is TextBox)
                return ((TextBox)control).Text;

            if (control is ComboBox)
                return ((ComboBox)control).SelectedValue;

            if (control is CheckBox)
                return ((CheckBox)control).Checked;

            try {
                return control.GetType().InvokeMember("Value", BindingFlags.GetProperty, null, control, null);
            } catch (MissingMethodException) { // Microsoft throws this
                return control.Text;
            } catch (MissingFieldException) { // Mono throws this
                return control.Text;
            }
        }

        /// <summary>
        /// Called when the cell editor could be about to lose focus. Time to commit the change
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        protected virtual void CellEditor_Validating(object sender, CancelEventArgs e) {
            this.cellEditEventArgs.Cancel = false;
            this.cellEditEventArgs.NewValue = this.GetControlValue(this.cellEditor);
            this.OnCellEditorValidating(this.cellEditEventArgs);

            if (this.cellEditEventArgs.Cancel) {
                this.cellEditEventArgs.Control.Select();
                e.Cancel = true;
            } else
                FinishCellEdit();
        }

        /// <summary>
        /// Return the bounds of the given cell
        /// </summary>
        /// <param name="item">The row to be edited</param>
        /// <param name="subItemIndex">The index of the cell to be edited</param>
        /// <returns>A Rectangle</returns>
        public virtual Rectangle CalculateCellBounds(OLVListItem item, int subItemIndex) {
            // We use ItemBoundsPortion.Label rather than ItemBoundsPortion.Item
            // since Label extends to the right edge of the cell, whereas Item gives just the
            // current text width.
            return this.CalculateCellBounds(item, subItemIndex, ItemBoundsPortion.Label);
        }

        /// <summary>
        /// Return the bounds of the given cell only until the edge of the current text
        /// </summary>
        /// <param name="item">The row to be edited</param>
        /// <param name="subItemIndex">The index of the cell to be edited</param>
        /// <returns>A Rectangle</returns>
        public virtual Rectangle CalculateCellTextBounds(OLVListItem item, int subItemIndex) {
            return this.CalculateCellBounds(item, subItemIndex, ItemBoundsPortion.ItemOnly);
        }

        private Rectangle CalculateCellBounds(OLVListItem item, int subItemIndex, ItemBoundsPortion portion) {
            // SubItem.Bounds works for every subitem, except the first.
            if (subItemIndex > 0)
                return item.SubItems[subItemIndex].Bounds;

            // For non detail views, we just use the requested portion
            Rectangle r = this.GetItemRect(item.Index, portion);
            if (r.Y < -10000000 || r.Y > 10000000) {
                r.Y = item.Bounds.Y;
            }
            if (this.View != View.Details)
                return r;

            // Finding the bounds of cell 0 should not be a difficult task, but it is. Problems:
            // 1) item.SubItem[0].Bounds is always the full bounds of the entire row, not just cell 0.
            // 2) if column 0 has been dragged to some other position, the bounds always has a left edge of 0.

            // We avoid both these problems by using the position of sides the column header to calculate
            // the sides of the cell
            Point sides = NativeMethods.GetScrolledColumnSides(this, 0);
            r.X = sides.X + 4;
            r.Width = sides.Y - sides.X - 5;

            return r;
        }

        /// <summary>
        /// Return a control that can be used to edit the value of the given cell.
        /// </summary>
        /// <param name="item">The row to be edited</param>
        /// <param name="subItemIndex">The index of the cell to be edited</param>
        /// <returns>A Control to edit the given cell</returns>
        protected virtual Control GetCellEditor(OLVListItem item, int subItemIndex) {
            OLVColumn column = this.GetColumn(subItemIndex);
            Object value = column.GetValue(item.RowObject) ?? this.GetFirstNonNullValue(column);

            // TODO: What do we do if value is still null here?

            // Ask the registry for an instance of the appropriate editor.
            Control editor = ObjectListView.EditorRegistry.GetEditor(item.RowObject, column, value);

            // Use a default editor if the registry can't create one for us.
            if (editor == null)
                editor = this.MakeDefaultCellEditor(column);

            return editor;
        }

        /// <summary>
        /// Get the first non-null value of the given column.
        /// At most 1000 rows will be considered.
        /// </summary>
        /// <param name="column"></param>
        /// <returns>The first non-null value, or null if no non-null values were found</returns>
        internal object GetFirstNonNullValue(OLVColumn column) {
            for (int i = 0; i < Math.Min(this.GetItemCount(), 1000); i++) {
                object value = column.GetValue(this.GetModelObject(i));
                if (value != null)
                    return value;
            }
            return null;
        }

        /// <summary>
        /// Return a TextBox that can be used as a default cell editor.
        /// </summary>
        /// <param name="column">What column does the cell belong to?</param>
        /// <returns></returns>
        protected virtual Control MakeDefaultCellEditor(OLVColumn column) {
            TextBox tb = new TextBox();
            if (column.AutoCompleteEditor)
                this.ConfigureAutoComplete(tb, column);
            return tb;
        }

        /// <summary>
        /// Configure the given text box to autocomplete unique values
        /// from the given column. At most 1000 rows will be considered.
        /// </summary>
        /// <param name="tb">The textbox to configure</param>
        /// <param name="column">The column used to calculate values</param>
        public void ConfigureAutoComplete(TextBox tb, OLVColumn column) {
            this.ConfigureAutoComplete(tb, column, 1000);
        }


        /// <summary>
        /// Configure the given text box to autocomplete unique values
        /// from the given column. At most 1000 rows will be considered.
        /// </summary>
        /// <param name="tb">The textbox to configure</param>
        /// <param name="column">The column used to calculate values</param>
        /// <param name="maxRows">Consider only this many rows</param>
        public void ConfigureAutoComplete(TextBox tb, OLVColumn column, int maxRows) {
            // Don't consider more rows than we actually have
            maxRows = Math.Min(this.GetItemCount(), maxRows);

            // Reset any existing autocomplete
            tb.AutoCompleteCustomSource.Clear();

            // CONSIDER: Should we use ClusteringStrategy here?

            // Build a list of unique values, to be used as autocomplete on the editor
            Dictionary<string, bool> alreadySeen = new Dictionary<string, bool>();
            List<string> values = new List<string>();
            for (int i = 0; i < maxRows; i++) {
                string valueAsString = column.GetStringValue(this.GetModelObject(i));
                if (!String.IsNullOrEmpty(valueAsString) && !alreadySeen.ContainsKey(valueAsString)) {
                    values.Add(valueAsString);
                    alreadySeen[valueAsString] = true;
                }
            }

            tb.AutoCompleteCustomSource.AddRange(values.ToArray());
            tb.AutoCompleteSource = AutoCompleteSource.CustomSource;
            tb.AutoCompleteMode = column.AutoCompleteEditorMode;
        }

        /// <summary>
        /// Stop editing a cell and throw away any changes.
        /// </summary>
        public virtual void CancelCellEdit() {
            if (!this.IsCellEditing)
                return;

            // Let the world know that the user has cancelled the edit operation
            this.cellEditEventArgs.Cancel = true;
            this.cellEditEventArgs.NewValue = this.GetControlValue(this.cellEditor);
            this.OnCellEditFinishing(this.cellEditEventArgs);

            // Now cleanup the editing process
            this.CleanupCellEdit();
        }

        /// <summary>
        /// If a cell edit is in progress, finish the edit.
        /// </summary>
        /// <returns>Returns false if the finishing process was cancelled
        /// (i.e. the cell editor is still on screen)</returns>
        /// <remarks>This method does not guarantee that the editing will finish. The validation
        /// process can cause the finishing to be aborted. Developers should check the return value
        /// or use IsCellEditing property after calling this method to see if the user is still
        /// editing a cell.</remarks>
        public virtual bool PossibleFinishCellEditing() {
            if (!this.IsCellEditing)
                return true;

            this.cellEditEventArgs.Cancel = false;
            this.cellEditEventArgs.NewValue = this.GetControlValue(this.cellEditor);
            this.OnCellEditorValidating(this.cellEditEventArgs);

            if (this.cellEditEventArgs.Cancel)
                return false;

            this.FinishCellEdit();

            return true;
        }

        /// <summary>
        /// Finish the cell edit operation, writing changed data back to the model object
        /// </summary>
        /// <remarks>This method does not trigger a Validating event, so it always finishes
        /// the cell edit.</remarks>
        public virtual void FinishCellEdit() {
            if (!this.IsCellEditing)
                return;

            this.cellEditEventArgs.Cancel = false;
            this.cellEditEventArgs.NewValue = this.GetControlValue(this.cellEditor);
            this.OnCellEditFinishing(this.cellEditEventArgs);

            // If someone doesn't cancel the editing process, write the value back into the model
            if (!this.cellEditEventArgs.Cancel) {
                this.cellEditEventArgs.Column.PutValue(this.cellEditEventArgs.RowObject, this.cellEditEventArgs.NewValue);
                this.RefreshItem(this.cellEditEventArgs.ListViewItem);
            }

            this.CleanupCellEdit();
        }

        /// <summary>
        /// Remove all trace of any existing cell edit operation
        /// </summary>
        protected virtual void CleanupCellEdit() {
            if (this.cellEditor == null)
                return;

            this.cellEditor.Validating -= new CancelEventHandler(CellEditor_Validating);

            Control soonToBeOldCellEditor = this.cellEditor;
            this.cellEditor = null;

            // Delay cleaning up the cell editor so that if we are immediately going to 
            // start a new cell edit (because the user pressed Tab) the new cell editor
            // has a chance to grab the focus. Without this, the ListView gains focus
            // momentarily (after the cell editor is remove and before the new one is created)
            // causing the list's selection to flash momentarily.
            EventHandler toBeRun = null;
            toBeRun = delegate(object sender, EventArgs e) {
                Application.Idle -= toBeRun;
                this.Controls.Remove(soonToBeOldCellEditor);
                this.Invalidate();

                if (!this.IsCellEditing) {
                    this.Select();
                    this.PauseAnimations(false);
                }
            };

            Application.Idle += toBeRun;
        }

        #endregion

        #region Hot row and cell handling

        /// <summary>
        /// Force the hot item to be recalculated
        /// </summary>
        public virtual void ClearHotItem() {
            this.UpdateHotItem(new Point(-1, -1));
        }

        /// <summary>
        /// Force the hot item to be recalculated
        /// </summary>
        public virtual void RefreshHotItem() {
            this.UpdateHotItem(this.PointToClient(Cursor.Position));
        }

        /// <summary>
        /// The mouse has moved to the given pt. See if the hot item needs to be updated
        /// </summary>
        /// <param name="pt">Where is the mouse?</param>
        /// <remarks>This is the main entry point for hot item handling</remarks>
        protected virtual void UpdateHotItem(Point pt) {
            this.UpdateHotItem(this.OlvHitTest(pt.X, pt.Y));
        }
        /// <summary>
        /// The mouse has moved to the given pt. See if the hot item needs to be updated
        /// </summary>
        /// <param name="hti"></param>
        /// <remarks>This is the main entry point for hot item handling</remarks>
        protected virtual void UpdateHotItem(OlvListViewHitTestInfo hti) {

            if (!this.UseHotItem && !this.UseHyperlinks)
                return;

            int newHotRow = hti.RowIndex;
            int newHotColumn = hti.ColumnIndex;
            HitTestLocation newHotCellHitLocation = hti.HitTestLocation;

            // In non-details view, we treat any hit on a row as if it were a hit
            // on column 0 -- which (effectively) it is!
            if (newHotRow >= 0 && this.View != View.Details)
                newHotColumn = 0;

            if (this.HotRowIndex == newHotRow &&
                this.HotColumnIndex == newHotColumn &&
                this.HotCellHitLocation == newHotCellHitLocation)
                return;

            // Trigger the hotitem changed event
            HotItemChangedEventArgs args = new HotItemChangedEventArgs();
            args.HotCellHitLocation = newHotCellHitLocation;
            args.HotColumnIndex = newHotColumn;
            args.HotRowIndex = newHotRow;
            args.OldHotCellHitLocation = this.HotCellHitLocation;
            args.OldHotColumnIndex = this.HotColumnIndex;
            args.OldHotRowIndex = this.HotRowIndex;
            this.OnHotItemChanged(args);

            // Update the state of the control
            this.HotRowIndex = newHotRow;
            this.HotColumnIndex = newHotColumn;
            this.HotCellHitLocation = newHotCellHitLocation;

            // If the event handler handled it complete, don't do anything else
            if (args.Handled)
                return;

            this.BeginUpdate();
            try {
                this.Invalidate();
                if (args.OldHotRowIndex != -1)
                    this.UnapplyHotItem(args.OldHotRowIndex);

                if (this.HotRowIndex != -1) {
                    // Virtual lists apply hot item style when fetching their rows
                    if (this.VirtualMode)
                        this.RedrawItems(this.HotRowIndex, this.HotRowIndex, true);
                    else
                        this.UpdateHotRow(this.HotRowIndex, this.HotColumnIndex, this.HotCellHitLocation, hti.Item);
                }

                if (this.UseHotItem && this.HotItemStyle != null && this.HotItemStyle.Overlay != null) {
                    this.RefreshOverlays();
                }
            } finally {
                this.EndUpdate();
            }
        }

        /// <summary>
        /// Update the given row using the current hot item information
        /// </summary>
        /// <param name="olvi"></param>
        protected virtual void UpdateHotRow(OLVListItem olvi) {
            this.UpdateHotRow(this.HotRowIndex, this.HotColumnIndex, this.HotCellHitLocation, olvi);
        }

        /// <summary>
        /// Update the given row using the given hot item information
        /// </summary>
        /// <param name="rowIndex"></param>
        /// <param name="columnIndex"></param>
        /// <param name="hitLocation"></param>
        /// <param name="olvi"></param>
        protected virtual void UpdateHotRow(int rowIndex, int columnIndex, HitTestLocation hitLocation, OLVListItem olvi) {
            if (rowIndex < 0 || columnIndex < 0)
                return;

            if (this.UseHyperlinks) {
                OLVColumn column = this.GetColumn(columnIndex);
                OLVListSubItem subItem = olvi.GetSubItem(columnIndex);
                if (column.Hyperlink && hitLocation == HitTestLocation.Text && !String.IsNullOrEmpty(subItem.Url)) {
                    this.ApplyCellStyle(olvi, columnIndex, this.HyperlinkStyle.Over);
                    this.Cursor = this.HyperlinkStyle.OverCursor ?? Cursors.Default;
                } else {
                    this.Cursor = Cursors.Default;
                }
            }

            if (this.UseHotItem) {
                if (!olvi.Selected) {
                    this.ApplyRowStyle(olvi, this.HotItemStyle);
                }
            }
        }

        /// <summary>
        /// Apply a style to the given row
        /// </summary>
        /// <param name="olvi"></param>
        /// <param name="style"></param>
        protected virtual void ApplyRowStyle(OLVListItem olvi, IItemStyle style) {
            if (style == null)
                return;

            if (this.FullRowSelect || this.View != View.Details) {
                if (style.Font != null)
                    olvi.Font = style.Font;

                if (style.FontStyle != FontStyle.Regular)
                    olvi.Font = new Font(olvi.Font ?? this.Font, style.FontStyle);

                if (!style.ForeColor.IsEmpty) {
                    if (olvi.UseItemStyleForSubItems)
                        olvi.ForeColor = style.ForeColor;
                    else {
                        foreach (ListViewItem.ListViewSubItem x in olvi.SubItems) {
                            x.ForeColor = style.ForeColor;
                        }
                    }
                }

                if (!style.BackColor.IsEmpty) {
                    if (olvi.UseItemStyleForSubItems)
                        olvi.BackColor = style.BackColor;
                    else {
                        foreach (ListViewItem.ListViewSubItem x in olvi.SubItems) {
                            x.BackColor = style.BackColor;
                        }
                    }
                }
            } else {
                olvi.UseItemStyleForSubItems = false;

                foreach (ListViewItem.ListViewSubItem x in olvi.SubItems) {
                    if (style.BackColor.IsEmpty)
                        x.BackColor = olvi.BackColor;
                    else
                        x.BackColor = style.BackColor;
                }

                this.ApplyCellStyle(olvi, 0, style);
            }
        }

        /// <summary>
        /// Apply a style to a cell
        /// </summary>
        /// <param name="olvi"></param>
        /// <param name="columnIndex"></param>
        /// <param name="style"></param>
        protected virtual void ApplyCellStyle(OLVListItem olvi, int columnIndex, IItemStyle style) {
            if (style == null)
                return;

            // Don't apply formatting to subitems when not in Details view
            if (this.View != View.Details && columnIndex > 0)
                return;

            olvi.UseItemStyleForSubItems = false;

            ListViewItem.ListViewSubItem subItem = olvi.SubItems[columnIndex];
            if (style.Font != null)
                subItem.Font = style.Font;

            if (style.FontStyle != FontStyle.Regular)
                subItem.Font = new Font(subItem.Font ?? olvi.Font ?? this.Font, style.FontStyle);

            if (!style.ForeColor.IsEmpty)
                subItem.ForeColor = style.ForeColor;

            if (!style.BackColor.IsEmpty)
                subItem.BackColor = style.BackColor;
        }

        /// <summary>
        /// Remove hot item styling from the given row
        /// </summary>
        /// <param name="index"></param>
        protected virtual void UnapplyHotItem(int index) {
            this.Cursor = Cursors.Default;
            // Virtual lists will apply the appropriate formatting when the row is fetched
            if (this.VirtualMode) {
                if (index < this.VirtualListSize)
                    this.RedrawItems(index, index, true);
            } else {
                OLVListItem olvi = this.GetItem(index);
                if (olvi != null) {
                    //this.PostProcessOneRow(index, index, olvi);
                    this.RefreshItem(olvi);
                }
            }
        }


        #endregion

        #region Drag and drop

        /// <summary>
        /// 
        /// </summary>
        /// <param name="e"></param>
        protected override void OnItemDrag(ItemDragEventArgs e) {
            base.OnItemDrag(e);

            if (this.DragSource == null)
                return;

            Object data = this.DragSource.StartDrag(this, e.Button, (OLVListItem)e.Item);
            if (data != null) {
                DragDropEffects effect = this.DoDragDrop(data, this.DragSource.GetAllowedEffects(data));
                this.DragSource.EndDrag(data, effect);
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="args"></param>
        protected override void OnDragEnter(DragEventArgs args) {
            base.OnDragEnter(args);

            if (this.DropSink != null)
                this.DropSink.Enter(args);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="args"></param>
        protected override void OnDragOver(DragEventArgs args) {
            base.OnDragOver(args);

            if (this.DropSink != null)
                this.DropSink.Over(args);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="args"></param>
        protected override void OnDragDrop(DragEventArgs args) {
            base.OnDragDrop(args);

            if (this.DropSink != null)
                this.DropSink.Drop(args);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="e"></param>
        protected override void OnDragLeave(EventArgs e) {
            base.OnDragLeave(e);

            if (this.DropSink != null)
                this.DropSink.Leave();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="args"></param>
        protected override void OnGiveFeedback(GiveFeedbackEventArgs args) {
            base.OnGiveFeedback(args);

            if (this.DropSink != null)
                this.DropSink.GiveFeedback(args);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="args"></param>
        protected override void OnQueryContinueDrag(QueryContinueDragEventArgs args) {
            base.OnQueryContinueDrag(args);

            if (this.DropSink != null)
                this.DropSink.QueryContinue(args);
        }

        #endregion

        #region Decorations and Overlays

        /// <summary>
        /// Add the given decoration to those on this list and make it appear
        /// </summary>
        /// <param name="decoration">The decoration</param>
        /// <remarks>
        /// A decoration scrolls with the listview. An overlay stays fixed in place.
        /// </remarks>
        public virtual void AddDecoration(IDecoration decoration) {
            if (decoration == null)
                return;
            this.Decorations.Add(decoration);
            this.Invalidate();
        }

        /// <summary>
        /// Add the given overlay to those on this list and make it appear
        /// </summary>
        /// <param name="overlay">The overlay</param>
        public virtual void AddOverlay(IOverlay overlay) {
            if (overlay == null)
                return;
            this.Overlays.Add(overlay);
            this.Invalidate();
        }

        /// <summary>
        /// Draw all the decorations
        /// </summary>
        /// <param name="g">A Graphics</param>
        /// <param name="drawnItems">The items that were redrawn and whose decorations should also be redrawn</param>
        protected virtual void DrawAllDecorations(Graphics g, List<OLVListItem> drawnItems) {
            g.TextRenderingHint = ObjectListView.TextRenderingHint;
            g.SmoothingMode = ObjectListView.SmoothingMode;

            Rectangle contentRectangle = this.ContentRectangle;

            if (this.HasEmptyListMsg && this.GetItemCount() == 0) {
                this.EmptyListMsgOverlay.Draw(this, g, contentRectangle);
            }

            // Let the drop sink draw whatever feedback it likes
            if (this.DropSink != null) {
                this.DropSink.DrawFeedback(g, contentRectangle);
            }

            // Draw our item and subitem decorations
            foreach (OLVListItem olvi in drawnItems) {
                if (olvi.HasDecoration) {
                    foreach (IDecoration d in olvi.Decorations) {
                        d.ListItem = olvi;
                        d.SubItem = null;
                        d.Draw(this, g, contentRectangle);
                    }
                }
                foreach (OLVListSubItem subItem in olvi.SubItems) {
                    if (subItem.HasDecoration) {
                        foreach (IDecoration d in subItem.Decorations) {
                            d.ListItem = olvi;
                            d.SubItem = subItem;
                            d.Draw(this, g, contentRectangle);
                        }
                    }
                }
                if (this.SelectedRowDecoration != null && olvi.Selected) {
                    this.SelectedRowDecoration.ListItem = olvi;
                    this.SelectedRowDecoration.SubItem = null;
                    this.SelectedRowDecoration.Draw(this, g, contentRectangle);
                }
            }

            // Now draw the specifically registered decorations
            foreach (IDecoration decoration in this.Decorations) {
                decoration.ListItem = null;
                decoration.SubItem = null;
                decoration.Draw(this, g, contentRectangle);
            }

            // Finally, draw any hot item decoration
            if (this.UseHotItem && this.HotItemStyle != null && this.HotItemStyle.Decoration != null) {
                IDecoration hotItemDecoration = this.HotItemStyle.Decoration;
                hotItemDecoration.ListItem = this.GetItem(this.HotRowIndex);
                if (hotItemDecoration.ListItem == null)
                    hotItemDecoration.SubItem = null;
                else
                    hotItemDecoration.SubItem = hotItemDecoration.ListItem.GetSubItem(this.HotColumnIndex);
                hotItemDecoration.Draw(this, g, contentRectangle);
            }

            // If we are in design mode, we don't want to use the glass panels,
            // so we draw the background overlays here
            if (this.DesignMode) {
                foreach (IOverlay overlay in this.Overlays) {
                    overlay.Draw(this, g, contentRectangle);
                }
            }
        }

        /// <summary>
        /// Is the given decoration shown on this list
        /// </summary>
        /// <param name="decoration">The overlay</param>
        public virtual bool HasDecoration(IDecoration decoration) {
            return this.Decorations.Contains(decoration);
        }

        /// <summary>
        /// Is the given overlay shown on this list?
        /// </summary>
        /// <param name="overlay">The overlay</param>
        public virtual bool HasOverlay(IOverlay overlay) {
            return this.Overlays.Contains(overlay);
        }

        /// <summary>
        /// Hide any overlays.
        /// </summary>
        /// <remarks>
        /// This is only a temporary hiding -- the overlays will be shown
        /// the next time the ObjectListView redraws.
        /// </remarks>
        public virtual void HideOverlays() {
            foreach (GlassPanelForm glassPanel in this.glassPanels) {
                glassPanel.HideGlass();
            }
        }

        /// <summary>
        /// Create and configure the empty list msg overlay
        /// </summary>
        protected virtual void InitializeEmptyListMsgOverlay() {
            TextOverlay textOverlay = new TextOverlay();
            textOverlay.Alignment = System.Drawing.ContentAlignment.MiddleCenter;
            textOverlay.TextColor = SystemColors.ControlDarkDark;
            textOverlay.BackColor = Color.BlanchedAlmond;
            textOverlay.BorderColor = SystemColors.ControlDark;
            textOverlay.BorderWidth = 2.0f;
            this.EmptyListMsgOverlay = textOverlay;
        }

        /// <summary>
        /// Initialize the standard image and text overlays
        /// </summary>
        protected virtual void InitializeStandardOverlays() {
            this.OverlayImage = new ImageOverlay();
            this.AddOverlay(this.OverlayImage);
            this.OverlayText = new TextOverlay();
            this.AddOverlay(this.OverlayText);
        }

        /// <summary>
        /// Make sure that any overlays are visible.
        /// </summary>
        public virtual void ShowOverlays() {
            // If we shouldn't show overlays, then don't create glass panels
            if (!this.ShouldShowOverlays())
                return;

            // Make sure that each overlay has its own glass panels
            if (this.Overlays.Count != this.glassPanels.Count) {
                foreach (IOverlay overlay in this.Overlays) {
                    GlassPanelForm glassPanel = this.FindGlassPanelForOverlay(overlay);
                    if (glassPanel == null) {
                        glassPanel = new GlassPanelForm();
                        glassPanel.Bind(this, overlay);
                        this.glassPanels.Add(glassPanel);
                    }
                }
            }
            foreach (GlassPanelForm glassPanel in this.glassPanels) {
                glassPanel.ShowGlass();
            }
        }

        private bool ShouldShowOverlays() {
            // If we are in design mode, we dont show the overlays
            if (this.DesignMode)
                return false;

            // If we are explicitly not using overlays, also don't show them
            if (!this.UseOverlays)
                return false;

            // If there are no overlays, guess...
            if (!this.HasOverlays)
                return false;

            // If we don't have 32-bit display, alpha blending doesn't work, so again, no overlays
            // TODO: This should actually figure out which screen(s) the control is on, and make sure
            // that each one is 32-bit.
            if (Screen.PrimaryScreen.BitsPerPixel < 32)
                return false;

            // Finally, we can show the overlays
            return true;
        }

        private GlassPanelForm FindGlassPanelForOverlay(IOverlay overlay) {
            return this.glassPanels.Find(delegate(GlassPanelForm x) { return x.Overlay == overlay; });
        }

        /// <summary>
        /// Refresh the display of the overlays
        /// </summary>
        public virtual void RefreshOverlays() {
            foreach (GlassPanelForm glassPanel in this.glassPanels) {
                glassPanel.Invalidate();
            }
        }

        /// <summary>
        /// Refresh the display of just one overlays
        /// </summary>
        public virtual void RefreshOverlay(IOverlay overlay) {
            GlassPanelForm glassPanel = this.FindGlassPanelForOverlay(overlay);
            if (glassPanel != null)
                glassPanel.Invalidate();
        }

        /// <summary>
        /// Remove the given decoration from this list
        /// </summary>
        /// <param name="decoration">The decoration to remove</param>
        public virtual void RemoveDecoration(IDecoration decoration) {
            if (decoration == null)
                return;
            this.Decorations.Remove(decoration);
            this.Invalidate();
        }

        /// <summary>
        /// Remove the given overlay to those on this list
        /// </summary>
        /// <param name="overlay">The overlay</param>
        public virtual void RemoveOverlay(IOverlay overlay) {
            if (overlay == null)
                return;
            this.Overlays.Remove(overlay);
            GlassPanelForm glassPanel = this.FindGlassPanelForOverlay(overlay);
            if (glassPanel != null) {
                this.glassPanels.Remove(glassPanel);
                glassPanel.Unbind();
                glassPanel.Dispose();
            }
        }

        #endregion

        #region Filtering

        /// <summary>
        /// Create a filter that will enact all the filtering currently installed
        /// on the visible columns.
        /// </summary>
        public virtual IModelFilter CreateColumnFilter() {
            List<IModelFilter> filters = new List<IModelFilter>();
            foreach (OLVColumn column in this.Columns) {
                IModelFilter filter = column.ValueBasedFilter;
                if (filter != null)
                    filters.Add(filter);
            }
            return (filters.Count == 0) ? null : new CompositeAllFilter(filters);
        }

        /// <summary>
        /// Do the actual work of filtering
        /// </summary>
        /// <param name="objects"></param>
        /// <param name="aModelFilter"></param>
        /// <param name="aListFilter"></param>
        /// <returns></returns>
        virtual protected IEnumerable FilterObjects(IEnumerable objects, IModelFilter aModelFilter, IListFilter aListFilter) {
            // Being cautious
            objects = objects ?? new ArrayList();

            // Tell the world to filter the objects. If they do so, don't do anything else
            FilterEventArgs args = new FilterEventArgs(objects);
            this.OnFilter(args);
            if (args.FilteredObjects != null)
                return args.FilteredObjects;

            // Apply a filter to the list as a whole
            if (aListFilter != null)
                objects = aListFilter.Filter(objects);

            // Apply the object filter if there is one
            if (aModelFilter != null) {
                ArrayList filteredObjects = new ArrayList();
                foreach (object model in objects) {
                    if (aModelFilter.Filter(model))
                        filteredObjects.Add(model);
                }
                objects = filteredObjects;
            }

            return objects;
        }

        /// <summary>
        /// Remove all column filtering.
        /// </summary>
        public virtual void ResetColumnFiltering() {
            foreach (OLVColumn column in this.Columns) {
                column.ValuesChosenForFiltering.Clear();
            }
            this.UpdateColumnFiltering();
        }

        /// <summary>
        /// Update the filtering of this ObjectListView based on the value filtering
        /// defined in each column
        /// </summary>
        public virtual void UpdateColumnFiltering() {
            this.ModelFilter = this.CreateColumnFilter();
        }

        /// <summary>
        /// When some setting related to filtering changes, this method is called.
        /// </summary>
        protected virtual void UpdateFiltering() {
            this.BuildList(true);
        }

        #endregion

        #region Implementation variables

        private Rectangle lastUpdateRectangle; // remember the update rect from the last WM_PAINT message
        private bool isOwnerOfObjects; // does this ObjectListView own the Objects collection?
        private bool hasIdleHandler; // has an Idle handler already been installed?
        private bool hasResizeColumnsHandler; // has an idle handler been installed which will handle column resizing?
        private bool isInWmPaintEvent; // is a WmPaint event currently being handled?
        private bool shouldDoCustomDrawing; // should the list do its custom drawing?
        private bool isMarqueSelecting; // Is a marque selection in progress?

        private List<GlassPanelForm> glassPanels = new List<GlassPanelForm>(); // The transparent panel that draws overlays
        Dictionary<string, bool> visitedUrlMap = new Dictionary<string, bool>(); // Which urls have been visited?

        #endregion
    }
}

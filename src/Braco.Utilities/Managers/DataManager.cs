﻿using Braco.Services.Abstractions;
using Braco.Utilities.Extensions;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;

namespace Braco.Utilities
{
	/// <summary>
	/// Data manager that performs some common functions on a data set.
	/// </summary>
	/// <typeparam name="T">Type of data to use.</typeparam>
    [PropertyChanged.AddINotifyPropertyChangedInterface]
    public class DataManager<T>
    {
        #region Fields

        private Func<IEnumerable<T>, IEnumerable<T>> lastMultiSort;
        private Func<T, bool> lastFilter;
        private string lastSearchQuery, lastSortColumn;

        private readonly IList<PropertyInfo> searchProperties = new List<PropertyInfo>();

        #endregion

        #region Properties

        /// <summary>
        /// Information about all columns that should be created
        /// for the type <typeparamref name="T"/>.
        /// </summary>
        public IList<ColumnInfo> ColumnInfos { get; }

        /// <summary>
        /// Information about display columns that should be created
        /// for the type <typeparamref name="T"/>.
        /// </summary>
        public IList<ColumnInfo> DisplayColumnInfos
            => ColumnInfos?.Where(c => c.DisplayNames?.Count > 0).ToList();

        private int page;

        /// <summary>
        /// Current page.
        /// </summary>
        public int Page
        {
            get => page;
            set
            {
                // If we aren't already on the given page...
                if (value != page)
                {
                    // Update the page
                    page = value;

                    // Update page data
                    UpdatePageData();

                    // Notify the listeners that the page has changed
                    PageChanged?.Invoke(this, new PageDataEventArgs(page, PageSize, NumPages));
                }
            }
        }

        private int pageSize;

        /// <summary>
        /// Number of records that can be placed on a page.
        /// </summary>
        public int PageSize
        {
            get => pageSize;
            set
            {
                // If the given page size is different and valid...
                if (value != pageSize && value > 0)
                {
                    // Update the size
                    pageSize = value;

                    // Return to first page if a page is being displayed
                    if (page > 0)
                        page = 1;

                    // Update page data
                    UpdatePageData();

                    // Notify the listeners that the page size has changed
                    PageSizeChanged?.Invoke(this, new PageDataEventArgs(Page, pageSize, NumPages));
                }
            }
        }

        /// <summary>
        /// Number of pages currently being displayed.
        /// </summary>
        public int NumPages => CalculateNumberOfPages(FilteredItems);

        /// <summary>
        /// Number of pages that can be displayed.
        /// </summary>
        public int MaxPages => CalculateNumberOfPages(AllItems);

        /// <summary>
        /// Data that is defined on the current page. If page is 0 or below,
        /// this will be empty.
        /// </summary>
        public ObservableCollection<T> PageItems { get; } = new ObservableCollection<T>();

        /// <summary>
        /// Items that are result of filtering using, for example, a search query.
        /// </summary>
        public ObservableCollection<T> FilteredItems { get; } = new ObservableCollection<T>();

        /// <summary>
        /// Data that is being managed. This is the only collection that should be altered from outside.
        /// Others are managed using the appropriate methods.
        /// </summary>
        public ObservableCollection<T> AllItems { get; } = new ObservableCollection<T>();

        /// <summary>
        /// Collection that contains data that was given when
        /// constructing the paged collection.
        /// </summary>
        public IList<T> OriginalCollection { get; } = new List<T>();

        #endregion

        #region Events

        /// <summary>
        /// Raised when the current page changes.
        /// </summary>
        public event EventHandler<PageDataEventArgs> PageChanged;

        /// <summary>
        /// Raised when number of pages changes.
        /// </summary>
        public event EventHandler<PageDataEventArgs> NumPagesChanged;

        /// <summary>
        /// Raised when page size changes.
        /// </summary>
        public event EventHandler<PageDataEventArgs> PageSizeChanged;

        #endregion

        #region Constructor

        /// <summary>
        /// Creates a new instance of <see cref="DataManager{T}"/> with
        /// optional starting data.
        /// </summary>
		/// <param name="localizer">Localizer to use inside the manager.</param>
        /// <param name="originalCollection">Starting data to use.</param>
		/// <param name="pageSize">Number of rows per page.</param>
		/// <param name="page">Page ordinal number.</param>
        public DataManager(ILocalizer localizer, IEnumerable<T> originalCollection = null, int? pageSize = 25, int? page = 1)
        {
            // Set data source if any items were provided
            SetDataSource(originalCollection);

            // Create the column infos
            ColumnInfos = new List<ColumnInfo>();

            // Loop through properties of the type T
            foreach (var property in typeof(T).GetProperties())
            {
                // Create info about the column
                var info = new ColumnInfo(property);

                // Fetch table column attribute
                var tableColumnAttribute = property.GetCustomAttribute<TableColumnAttribute>();

                // If it exists...
                if (tableColumnAttribute != null)
                {
                    // Extract display index
                    info.DisplayIndex = tableColumnAttribute.DisplayIndex;

                    // Extract resource name
                    info.LocalizationKey = tableColumnAttribute.ResourceName;

                    // Get all display names from the localizer
                    info.DisplayNames = localizer.GetAllValues(info.LocalizationKey);
                }

                // Fetch search attribute
                var searchAttribute = property.GetCustomAttribute<SearchAttribute>();

                // If it exists...
                if (searchAttribute != null)
                    // Define that this property is included in searches
                    searchProperties.Add(property);

                // Add the column information into the list
                ColumnInfos.Add(info);
            }

            // If the page size was given...
            if (pageSize.HasValue)
                // Set it
                PageSize = pageSize.Value;

            // If the starting page was given...
            if (page.HasValue)
                // Set it
                Page = page.Value;
        }

        #endregion

        #region Methods

        /// <summary>
        /// Invokes property changed for <see cref="NumPages"/> and
        /// raises <see cref="NumPagesChanged"/> event.
        /// </summary>
        public void NotifyNumPagesChanged()
        {
            // Invoke property changed for the property
            ReflectionUtilities.InvokePropertyChanged(this, nameof(NumPages));

            // Notify the listeners that the number of pages has changed
            NumPagesChanged?.Invoke(this, new PageDataEventArgs(Page, PageSize, NumPages));
        }

        /// <summary>
        /// Applies all alterations that have been previously applied
        /// using <see cref="Filter(Func{T, bool})"/>, <see cref="Search(string)"/>
        /// and <see cref="Sort(string)"/> methods.
        /// </summary>
        public void UpdateAlterations()
        {
            // Get number of pages before altering anything
            var currentNumberOfPages = NumPages;

            // Get all of the items that we will be filtering
            IEnumerable<T> filteredItems = AllItems;

            // If there is some search query...
            if (lastSearchQuery.IsNotNullOrWhiteSpace())
                // Filter items to those where...
                filteredItems = filteredItems.Where(item =>
                    // Search query matches some searched properties' value(s)
                    lastSearchQuery.PartialSearch(searchProperties.Select(prop =>
                        prop.GetValue(item)?.ToString()).ToArray()
                    )
                );

            // If a filter was assigned...
            if (lastFilter != null)
                // Apply it on the remaining items
                filteredItems = filteredItems.Where(lastFilter);

            // If the data should be sorted...
            if (lastSortColumn != null)
            {
                // Get the display column from the info collection
                var column = GetDisplayColumn(lastSortColumn);

                // Define the new sort direction
                column.SortDirection = column.SortDirection switch
                {
                    SortDirection.None => SortDirection.Ascending,
                    SortDirection.Ascending => SortDirection.Descending,
                    SortDirection.Descending => SortDirection.Ascending,
                    _ => throw new ArgumentOutOfRangeException(nameof(column.SortDirection)),
                };

                // Sort the items based on new direction
                switch (column.SortDirection)
                {
                    case SortDirection.Ascending:
                        filteredItems = filteredItems.OrderBy(item => column.Property.GetValue(item)).ToList();
                        break;

                    case SortDirection.Descending:
                        filteredItems = filteredItems.OrderByDescending(item => column.Property.GetValue(item)).ToList();
                        break;

                    // If there should be no sorting...
                    case SortDirection.None:
                    default:
                        // Bail
                        break;
                }
            }

            // If the data should be sorted by multiple columns...
            if (lastMultiSort != null)
                // Apply it on the remaining items
                filteredItems = lastMultiSort(filteredItems);

            // Update the filtered items
            FilteredItems.RenewData(filteredItems);

            // Update page data
            UpdatePageData();

            // If the number of pages changed...
            if (NumPages != currentNumberOfPages)
                // Signal it
                NotifyNumPagesChanged();
        }

        /// <summary>
        /// Sets the given items as data source that
        /// will be managed.
        /// </summary>
        /// <param name="dataSource">Collection of items to manage.</param>
        public void SetDataSource(IEnumerable<T> dataSource)
        {
            // Clear collections 
            PageItems.Clear();
            OriginalCollection.Clear();
            AllItems.Clear();
            FilteredItems.Clear();

            // If some data was provided...
            dataSource.ForEach(item =>
            {
                // Add it to original collection, all items and filtered items
                OriginalCollection.Add(item);
                AllItems.Add(item);
                FilteredItems.Add(item);
            });

            // Update page data if necessary
            UpdateAlterations();
        }

        /// <summary>
        /// Filters data based on the given filter.
        /// </summary>
        /// <param name="filter">Filter to use for filtering the data.</param>
        public void Filter(Func<T, bool> filter)
        {
            // If nothing is provided...
            if (filter == null)
                // Bail
                return;

            // Keep track of the filter
            lastFilter = filter;

            // Apply all of the alterations at once
            UpdateAlterations();
        }

        /// <summary>
        /// Performs fuzzy search on the given query using
        /// properties on type <typeparamref name="T"/>
        /// decorated with <see cref="SearchAttribute"/>.
        /// </summary>
        /// <param name="query">Query to check the properties against.</param>
        public void Search(string query)
        {
            // Keep track of the query
            lastSearchQuery = query;

            // Apply all of the alterations at once
            UpdateAlterations();
        }

        /// <summary>
        /// Sorts items for the specified column name.
        /// </summary>
        /// <param name="columnName">Name of the column used for sorting.</param>
        public void Sort(string columnName)
        {
            // Keep track of the column name
            lastSortColumn = columnName;

            // Disable multi sorting
            lastMultiSort = null;

            // Apply all of the alterations at once
            UpdateAlterations();
        }

        /// <summary>
        /// Performs a sort of multiple columns.
        /// </summary>
        /// <param name="multiSort">Method that does the sorting.</param>
        public void MultiSort(Func<IEnumerable<T>, IEnumerable<T>> multiSort)
        {
            // If the given sort method is the same...
            if (multiSort == lastMultiSort)
                // Bail
                return;

            // Keep track of the method
            lastMultiSort = multiSort;

            // Disable sorting
            lastSortColumn = null;

            // Apply all of the alterations at once
            UpdateAlterations();
        }

        /// <summary>
        /// Gets a display column with given display name.
        /// </summary>
        /// <param name="columnName">Display name of the column to find.</param>
        /// <returns></returns>
        public ColumnInfo GetDisplayColumn(string columnName)
            => DisplayColumnInfos.FirstOrDefault(c => c.DisplayNames.Contains(columnName));

        private void UpdatePageData()
        {
            // If an invalid page is given...
            if (Page <= 0)
                // Clear the data
                PageItems.Clear();

            // Otherwise...
            else
                // Update data with items from the given page
                PageItems.RenewData(FilteredItems.Skip(PageSize * (Page - 1)).Take(PageSize));
        }

        private int CalculateNumberOfPages(ICollection<T> collection)
        {
            // If the page size is invalid...
            if (PageSize <= 0)
                // Just return invalid number
                return -1;

            // Get the number of pages
            var numPages = collection.Count / PageSize;

            // If there is an overflow...
            if (collection.Count % PageSize != 0)
                // That means that another page is available
                numPages++;

            // Return the total number of pages
            return numPages;
        }

        #endregion
    }
}
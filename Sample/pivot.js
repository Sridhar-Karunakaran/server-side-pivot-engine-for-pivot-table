var pivotGridObj = new ej.pivotview.PivotView({
    dataSourceSettings: {
        url: window.location.protocol + '//' + window.location.host + '/api/pivot/post',
        index: 'Sample-Csv',
        mode: 'Server',
        type: 'JSON',
        expandAll: false,
        rows: [{ name: 'Country', caption: 'Country' }, { name: 'Sector', caption: 'Sector' }],
        columns: [{ name: 'Year', caption: 'Year' }, { name: 'PowerUnits', caption: 'PowerUnits' }],
        values: [{ name: 'Sold', caption: 'Sold' }, { name: 'Price', caption: 'Price' }],
        filters: []
    },
    showGroupingBar: true,
    showFieldList: true,
    height: 500,
    width: 1000
});
pivotGridObj.appendTo('#PivotView');

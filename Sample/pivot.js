var pivotGridObj = new ej.pivotview.PivotView({
    dataSourceSettings: {
        // Custom Data Server endpoint and index name (see PivotController/Flexmonster_Data_Server_Phase1_Specification.md)
        url: 'http://localhost:5285/api/pivot/post',
        index: 'Sales-Data',
        mode: 'Server',
        type: 'JSON',
        expandAll: false,
        rows: [{
            name: 'ProductID', caption: 'Product ID'
        }],
        enableSorting: true,
        formatSettings: [{
            name: 'Price',
            format: 'C'
        }],
        columns: [{
            name: 'Year', caption: 'Production Year'
        }],
        values: [{
            name: 'Sold', caption: 'Units Sold'
        },
        {
            name: 'Price', caption: 'Sold Amount'
        },],
        allowMemberFilter: true
    },
    enableVirtualization: true,
    enableValueSorting: true,
    allowCalculatedField: true,
    showGroupingBar: true,
    showFieldList: true,
    height: 500,
    width: 1000
});
pivotGridObj.appendTo('#PivotView');
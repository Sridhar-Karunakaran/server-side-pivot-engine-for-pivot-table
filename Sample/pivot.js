// Index name sent with every request via query string (spec §7, clarified):
// the Syncfusion client does not serialize custom dataSourceSettings properties
// into the POST payload, so the index rides on the URL instead. Requests
// without it fall back to the server's default index (DefaultIndexId).
var PIVOT_INDEX = 'Sales-Json';
var PIVOT_URL = 'https://localhost:44350/api/pivot/post?index=' + PIVOT_INDEX;

var pivotGridObj = new ej.pivotview.PivotView({
    dataSourceSettings: {
        url: PIVOT_URL,
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
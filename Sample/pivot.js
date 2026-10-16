// Custom Data Server (Phase 1) client configuration.
// The server endpoint and the index name are set here; the index name is sent
// on every request so the server routes to the registered index (spec §3.2/§10).
var PIVOT_INDEX = 'Sample-Csv';
var pivotGridObj = new ej.pivotview.PivotView({
    dataSourceSettings: {
        url: 'http://localhost:5285/api/pivot/post',
        index: PIVOT_INDEX,
        mode: 'Server',
        type: 'JSON',
        expandAll: false,
        rows: [{
            name: 'Country', caption: 'Country'
        }],
        enableSorting: true,
        columns: [{
            name: 'Year', caption: 'Year'
        }],
        values: [{
            name: 'Amount', caption: 'Amount'
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
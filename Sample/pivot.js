var pivotGridObj = new ej.pivotview.PivotView({
    dataSourceSettings: {
        url: 'http://localhost:8765/api/pivot/post',
        mode: 'Server',
        type: 'JSON',
        expandAll: false,
        // rows: [{
        //     name: 'Sector', caption: 'Sector'
        // }],
        // columns: [{
        //     name: 'EnerType', caption: 'Energy Type'
        // }],
        // values: [{
        //     name: 'PowUnits', caption: 'Power Units'
        // },
        // {
        //     name: 'ProCost', caption: 'Production Cost'
        // }],
        enableSorting: true,
        formatSettings: [{
            name: 'ProCost',
            format: 'N0'
        }],
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
var pivotGridObj = new ej.pivotview.PivotView({
    dataSourceSettings: {
        url: 'http://localhost:7800/api/pivot/post',
        mode: 'Server',
        type: 'JSON',
        rows: [{ name: 'Sector' }],
        columns: [{ name: 'EnerType', caption: 'Energy Source' }],
        values: [
            { name: 'PowUnits', caption: 'Power Units' },
        ]
    },
    allowCalculatedField: true,
    showGroupingBar: true,
    showFieldList: true,
    height: 500,
    width: 1000
});
pivotGridObj.appendTo('#PivotView');
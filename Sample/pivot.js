var pivotGridObj = new ej.pivotview.PivotView({
    dataSourceSettings: {
        url: window.location.protocol + '//' + window.location.host + '/api/pivot/post',
        mode: 'Server',
        filters: []
    },
    showGroupingBar: true,
    showFieldList: true,
    height: 500,
    width: 1000
});
pivotGridObj.appendTo('#PivotView');

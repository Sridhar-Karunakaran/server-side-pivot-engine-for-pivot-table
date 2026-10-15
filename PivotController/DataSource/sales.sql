CREATE TABLE Sales (
    Region TEXT NOT NULL,
    Country TEXT NOT NULL,
    ItemType TEXT NOT NULL,
    SalesChannel TEXT NOT NULL,
    OrderDate TEXT NOT NULL,
    UnitsSold INTEGER NOT NULL,
    TotalRevenue REAL NOT NULL,
    TotalCost REAL NOT NULL,
    TotalProfit REAL NOT NULL
);

INSERT INTO Sales VALUES
('Middle East and North Africa', 'Libya', 'Cosmetics', 'Offline', '2014-10-18', 8446, 3692591.20, 2224085.18, 1468506.02),
('North America', 'Canada', 'Vegetables', 'Online', '2011-11-07', 3018, 464953.08, 274426.74, 190526.34),
('Europe', 'France', 'Office Supplies', 'Online', '2015-01-12', 5120, 333824.00, 268748.80, 65075.20),
('Asia', 'India', 'Household', 'Offline', '2016-08-22', 4210, 2810280.20, 2113001.10, 697279.10),
('Sub-Saharan Africa', 'Kenya', 'Beverages', 'Online', '2017-03-04', 7350, 348243.00, 234244.50, 113998.50);

IF OBJECT_ID(N'dbo.OrderItems', N'U') IS NOT NULL
BEGIN
    DROP TABLE dbo.OrderItems;
END;
GO

IF OBJECT_ID(N'dbo.Payments', N'U') IS NOT NULL
BEGIN
    DROP TABLE dbo.Payments;
END;
GO

IF OBJECT_ID(N'dbo.Shipments', N'U') IS NOT NULL
BEGIN
    DROP TABLE dbo.Shipments;
END;
GO

IF OBJECT_ID(N'dbo.OrderStatusHistory', N'U') IS NOT NULL
BEGIN
    DROP TABLE dbo.OrderStatusHistory;
END;
GO

IF OBJECT_ID(N'dbo.Products', N'U') IS NOT NULL
BEGIN
    DROP TABLE dbo.Products;
END;
GO

IF OBJECT_ID(N'dbo.Orders', N'U') IS NOT NULL
BEGIN
    DROP TABLE dbo.Orders;
END;
GO

CREATE TABLE dbo.Orders
(
    Id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    CustomerName NVARCHAR(200) NOT NULL,
    TotalAmount DECIMAL(18,2) NOT NULL,
    Status NVARCHAR(50) NOT NULL,
    CreatedUtc DATETIME2(3) NOT NULL
);
GO

CREATE TABLE dbo.Products
(
    Id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    Name NVARCHAR(200) NOT NULL,
    SKU NVARCHAR(100) NOT NULL,
    Price DECIMAL(18,2) NOT NULL,
    IsActive BIT NOT NULL DEFAULT 1,
    CreatedUtc DATETIME2(3) NOT NULL
);
GO

CREATE TABLE dbo.OrderItems
(
    Id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    OrderId INT NOT NULL,
    ProductId INT NOT NULL,
    Quantity INT NOT NULL,
    UnitPrice DECIMAL(18,2) NOT NULL,

    CONSTRAINT FK_OrderItems_Orders
        FOREIGN KEY (OrderId)
        REFERENCES dbo.Orders(Id),

    CONSTRAINT FK_OrderItems_Products
        FOREIGN KEY (ProductId)
        REFERENCES dbo.Products(Id)
);
GO

CREATE TABLE dbo.Payments
(
    Id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    OrderId INT NOT NULL,
    PaymentMethod NVARCHAR(50) NOT NULL,
    Amount DECIMAL(18,2) NOT NULL,
    PaidUtc DATETIME2(3) NULL,
    Status NVARCHAR(50) NOT NULL,

    CONSTRAINT FK_Payments_Orders
        FOREIGN KEY (OrderId)
        REFERENCES dbo.Orders(Id)
);
GO

CREATE TABLE dbo.Shipments
(
    Id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    OrderId INT NOT NULL,
    AddressLine1 NVARCHAR(200) NOT NULL,
    City NVARCHAR(100) NOT NULL,
    PostalCode NVARCHAR(20) NOT NULL,
    Country NVARCHAR(100) NOT NULL,
    ShippedUtc DATETIME2(3) NULL,
    Status NVARCHAR(50) NOT NULL,

    CONSTRAINT FK_Shipments_Orders
        FOREIGN KEY (OrderId)
        REFERENCES dbo.Orders(Id)
);
GO

CREATE TABLE dbo.OrderStatusHistory
(
    Id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    OrderId INT NOT NULL,
    OldStatus NVARCHAR(50) NULL,
    NewStatus NVARCHAR(50) NOT NULL,
    ChangedUtc DATETIME2(3) NOT NULL,
    ChangedBy NVARCHAR(200) NULL,

    CONSTRAINT FK_OrderStatusHistory_Orders
        FOREIGN KEY (OrderId)
        REFERENCES dbo.Orders(Id)
);
GO


DECLARE @OrderIds TABLE
(
    CustomerName NVARCHAR(200) NOT NULL PRIMARY KEY,
    OrderId INT NOT NULL
);

INSERT INTO dbo.Orders (CustomerName, TotalAmount, Status, CreatedUtc)
OUTPUT inserted.CustomerName, inserted.Id INTO @OrderIds (CustomerName, OrderId)
VALUES
    (N'Acme Retail', 149.99, N'Pending', SYSUTCDATETIME()),
    (N'Northwind Traders', 980.50, N'Processing', DATEADD(MINUTE, -20, SYSUTCDATETIME())),
    (N'Contoso Ltd', 45.00, N'Completed', DATEADD(DAY, -1, SYSUTCDATETIME()));

DECLARE @ProductIds TABLE
(
    SKU NVARCHAR(100) NOT NULL PRIMARY KEY,
    ProductId INT NOT NULL
);

INSERT INTO dbo.Products (Name, SKU, Price, IsActive, CreatedUtc)
OUTPUT inserted.SKU, inserted.Id INTO @ProductIds (SKU, ProductId)
VALUES
    (N'USB-C Hub', N'HUB-USB-C-7IN1', 49.99, 1, DATEADD(DAY, -10, SYSUTCDATETIME())),
    (N'Noise Cancelling Headphones', N'AUD-NC-1000', 199.00, 1, DATEADD(DAY, -8, SYSUTCDATETIME())),
    (N'Ergonomic Keyboard', N'KB-ERG-01', 89.50, 1, DATEADD(DAY, -6, SYSUTCDATETIME())),
    (N'4K Monitor 27"', N'MON-4K-27', 329.00, 1, DATEADD(DAY, -4, SYSUTCDATETIME()));

INSERT INTO dbo.OrderItems (OrderId, ProductId, Quantity, UnitPrice)
SELECT o.OrderId, p.ProductId, v.Quantity, v.UnitPrice
FROM
(
    VALUES
        (N'Acme Retail', N'HUB-USB-C-7IN1', 1, CAST(49.99 AS DECIMAL(18,2))),
        (N'Acme Retail', N'KB-ERG-01', 1, CAST(89.50 AS DECIMAL(18,2))),
        (N'Northwind Traders', N'AUD-NC-1000', 2, CAST(199.00 AS DECIMAL(18,2))),
        (N'Northwind Traders', N'MON-4K-27', 1, CAST(329.00 AS DECIMAL(18,2))),
        (N'Contoso Ltd', N'HUB-USB-C-7IN1', 1, CAST(45.00 AS DECIMAL(18,2)))
) AS v(CustomerName, SKU, Quantity, UnitPrice)
INNER JOIN @OrderIds AS o ON o.CustomerName = v.CustomerName
INNER JOIN @ProductIds AS p ON p.SKU = v.SKU;

INSERT INTO dbo.Payments (OrderId, PaymentMethod, Amount, PaidUtc, Status)
SELECT o.OrderId, v.PaymentMethod, v.Amount, v.PaidUtc, v.Status
FROM
(
    VALUES
        (N'Acme Retail', N'Card', CAST(149.99 AS DECIMAL(18,2)), CAST(NULL AS DATETIME2(3)), N'Pending'),
        (N'Northwind Traders', N'Wire', CAST(727.00 AS DECIMAL(18,2)), DATEADD(MINUTE, -15, SYSUTCDATETIME()), N'Captured'),
        (N'Contoso Ltd', N'Card', CAST(45.00 AS DECIMAL(18,2)), DATEADD(DAY, -1, DATEADD(MINUTE, 5, SYSUTCDATETIME())), N'Captured')
) AS v(CustomerName, PaymentMethod, Amount, PaidUtc, Status)
INNER JOIN @OrderIds AS o ON o.CustomerName = v.CustomerName;

INSERT INTO dbo.Shipments (OrderId, AddressLine1, City, PostalCode, Country, ShippedUtc, Status)
SELECT o.OrderId, v.AddressLine1, v.City, v.PostalCode, v.Country, v.ShippedUtc, v.Status
FROM
(
    VALUES
        (N'Acme Retail', N'100 Main Street', N'London', N'SW1A 1AA', N'UK', CAST(NULL AS DATETIME2(3)), N'Pending'),
        (N'Northwind Traders', N'42 Harbor Road', N'Liverpool', N'L1 8JQ', N'UK', DATEADD(MINUTE, -5, SYSUTCDATETIME()), N'Shipped'),
        (N'Contoso Ltd', N'7 Orchard Ave', N'Manchester', N'M1 4BT', N'UK', DATEADD(DAY, -1, DATEADD(HOUR, 2, SYSUTCDATETIME())), N'Delivered')
) AS v(CustomerName, AddressLine1, City, PostalCode, Country, ShippedUtc, Status)
INNER JOIN @OrderIds AS o ON o.CustomerName = v.CustomerName;

INSERT INTO dbo.OrderStatusHistory (OrderId, OldStatus, NewStatus, ChangedUtc, ChangedBy)
SELECT o.OrderId, v.OldStatus, v.NewStatus, v.ChangedUtc, v.ChangedBy
FROM
(
    VALUES
        (N'Acme Retail', CAST(NULL AS NVARCHAR(50)), N'Pending', DATEADD(MINUTE, -2, SYSUTCDATETIME()), N'seed-script'),
        (N'Northwind Traders', CAST(NULL AS NVARCHAR(50)), N'Pending', DATEADD(MINUTE, -30, SYSUTCDATETIME()), N'seed-script'),
        (N'Northwind Traders', N'Pending', N'Processing', DATEADD(MINUTE, -20, SYSUTCDATETIME()), N'worker-1'),
        (N'Contoso Ltd', CAST(NULL AS NVARCHAR(50)), N'Pending', DATEADD(DAY, -1, DATEADD(HOUR, -1, SYSUTCDATETIME())), N'seed-script'),
        (N'Contoso Ltd', N'Pending', N'Completed', DATEADD(DAY, -1, SYSUTCDATETIME()), N'worker-2')
) AS v(CustomerName, OldStatus, NewStatus, ChangedUtc, ChangedBy)
INNER JOIN @OrderIds AS o ON o.CustomerName = v.CustomerName;
GO

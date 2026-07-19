CREATE TABLE dbo.Orders
(
    Id int NOT NULL,
    Number nvarchar(40) NOT NULL
);
GO

CREATE OR ALTER PROCEDURE dbo.UpdateOrder
AS
BEGIN
    SELECT Id, Number FROM dbo.Orders;
    UPDATE dbo.Orders SET Number = Number WHERE Id > 0;
END;

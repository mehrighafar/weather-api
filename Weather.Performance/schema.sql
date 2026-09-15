-- Run this in the benchmark database before the first run.
-- The benchmark also creates these tables automatically when permissions allow it.

IF OBJECT_ID(N'dbo.BenchmarkAppendRows', N'U') IS NULL
BEGIN
	CREATE TABLE dbo.BenchmarkAppendRows
	(
		Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_BenchmarkAppendRows PRIMARY KEY,
		WorkKey int NOT NULL,
		Value int NOT NULL,
		CreatedAt datetime2(3) NOT NULL
	);
END;

IF OBJECT_ID(N'dbo.BenchmarkUpsertRows', N'U') IS NULL
BEGIN
	CREATE TABLE dbo.BenchmarkUpsertRows
	(
		Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_BenchmarkUpsertRows PRIMARY KEY,
		WorkKey int NOT NULL,
		Value int NOT NULL,
		CreatedAt datetime2(3) NOT NULL
	);
	CREATE UNIQUE INDEX UX_BenchmarkUpsertRows_WorkKey
		ON dbo.BenchmarkUpsertRows(WorkKey);
END;

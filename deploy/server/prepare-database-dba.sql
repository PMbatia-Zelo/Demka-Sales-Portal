-- ONE-TIME setup by a SQL Server administrator (sysadmin) on SQLEXPRESS, e.g. in SSMS on the VM.
-- Needed because Azure Run Command runs as NT AUTHORITY\SYSTEM, which is not a SQL administrator on this server.
-- Run AFTER the IIS app pool "DemkaPortal" exists (Script 4 / 02-create-site.ps1). Safe to run again.
--
-- What it does, and nothing else:
--   1. Creates the empty DemkaPortal database (tables are created later by the deployment).
--   2. Gives the website (IIS APPPOOL\DemkaPortal) read/write on DemkaPortal only.
--   3. Makes SYSTEM owner of DemkaPortal only, so deployments and backups run from Azure Run Command
--      can create/update the portal's tables and back it up. SYSTEM gets no rights on any other database.
USE [master];
IF DB_ID(N'DemkaPortal') IS NULL
    CREATE DATABASE [DemkaPortal];
GO
ALTER DATABASE [DemkaPortal] SET RECOVERY SIMPLE;
GO
IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'IIS APPPOOL\DemkaPortal')
    CREATE LOGIN [IIS APPPOOL\DemkaPortal] FROM WINDOWS;
IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'NT AUTHORITY\SYSTEM')
    CREATE LOGIN [NT AUTHORITY\SYSTEM] FROM WINDOWS;
GO
USE [DemkaPortal];
IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'IIS APPPOOL\DemkaPortal')
    CREATE USER [IIS APPPOOL\DemkaPortal] FOR LOGIN [IIS APPPOOL\DemkaPortal];
ALTER ROLE db_datareader ADD MEMBER [IIS APPPOOL\DemkaPortal];
ALTER ROLE db_datawriter ADD MEMBER [IIS APPPOOL\DemkaPortal];
IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'NT AUTHORITY\SYSTEM')
    CREATE USER [NT AUTHORITY\SYSTEM] FOR LOGIN [NT AUTHORITY\SYSTEM];
ALTER ROLE db_owner ADD MEMBER [NT AUTHORITY\SYSTEM];
GO
SELECT dp.name AS [User], r.name AS [Role]
FROM sys.database_role_members m
JOIN sys.database_principals r ON r.principal_id = m.role_principal_id
JOIN sys.database_principals dp ON dp.principal_id = m.member_principal_id
WHERE dp.name IN (N'IIS APPPOOL\DemkaPortal', N'NT AUTHORITY\SYSTEM');

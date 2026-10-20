-- Gives the IIS application pool read/write access to the portal database (no rights to change tables).
-- Run by deploy-release.ps1 after the database exists. Safe to run again.
-- $(PoolName) is set by the script: DemkaPortal (live) or DemkaPortalTest (test). $(DatabaseName) likewise.
-- If the account running this cannot create logins (e.g. SYSTEM via Azure Run Command, which is db_owner of the
-- portal database only), the login must already exist: a SQL admin creates it once with prepare-database-dba.sql.
USE [master];
IF HAS_PERMS_BY_NAME(NULL, NULL, 'ALTER ANY LOGIN') = 1
   AND NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'IIS APPPOOL\$(PoolName)')
    CREATE LOGIN [IIS APPPOOL\$(PoolName)] FROM WINDOWS;
GO
USE [$(DatabaseName)];
IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'IIS APPPOOL\$(PoolName)')
    CREATE USER [IIS APPPOOL\$(PoolName)] FOR LOGIN [IIS APPPOOL\$(PoolName)];
ALTER ROLE db_datareader ADD MEMBER [IIS APPPOOL\$(PoolName)];
ALTER ROLE db_datawriter ADD MEMBER [IIS APPPOOL\$(PoolName)];
GO

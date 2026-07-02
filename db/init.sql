IF NOT EXISTS (SELECT * FROM sys.databases WHERE name = 'SkillsCatalog')
BEGIN
    CREATE DATABASE SkillsCatalog;
END
GO

USE SkillsCatalog;
GO

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Skills')
BEGIN
    CREATE TABLE Skills (
        Id INT IDENTITY(1,1) PRIMARY KEY,
        CanonicalName NVARCHAR(100) NOT NULL UNIQUE,
        Category NVARCHAR(50) NOT NULL,
        Synonyms NVARCHAR(MAX) NOT NULL
    );
END
GO

-- Seed Skills
TRUNCATE TABLE Skills;
INSERT INTO Skills (CanonicalName, Category, Synonyms) VALUES
('JavaScript', 'Frontend', 'js, javascript, es6, es7'),
('TypeScript', 'Frontend', 'ts, typescript'),
('Angular', 'Frontend', 'angular, angularjs, ng, angular19, angular20'),
('React', 'Frontend', 'react, reactjs, react.js, nextjs, next.js'),
('Vue.js', 'Frontend', 'vue, vuejs, vue.js, nuxt'),
('C#', 'Backend', 'c#, csharp, .net, dotnet, asp.net, dotnet core'),
('Python', 'Backend', 'python, py, django, flask, fastapi'),
('Node.js', 'Backend', 'node, nodejs, node.js, express, nestjs'),
('SQL Server', 'Database', 'sql server, mssql, t-sql, microsoft sql server'),
('Azure SQL', 'Database', 'azure sql, azure sql database'),
('Cosmos DB', 'Database', 'cosmos, cosmosdb, cosmos db, nosql'),
('Docker', 'DevOps', 'docker, container, containers'),
('Kubernetes', 'DevOps', 'k8s, kubernetes, helm'),
('AWS', 'Cloud', 'aws, amazon web services, s3, ec2, lambda'),
('Azure', 'Cloud', 'azure, microsoft azure, app services, functions');
GO

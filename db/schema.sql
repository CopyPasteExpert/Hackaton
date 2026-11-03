-- Schema for competition results. Run after `dotnet ef database update`.
-- Data comes from MON protocols via Tracker.Importer (extract, then import).
-- Re-running this file empties the table.

CREATE DATABASE IF NOT EXISTS tracker
    CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;

USE tracker;

DROP TABLE IF EXISTS competition_results;

CREATE TABLE competition_results (
    id INT AUTO_INCREMENT PRIMARY KEY,
    participant_name VARCHAR(200) NOT NULL,
    competition_name VARCHAR(200) NOT NULL,
    competition_year INT NOT NULL,
    score DECIMAL(6,2) NULL,
    grade INT NULL,
    place INT NULL,
    source_url VARCHAR(500) NULL
) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;

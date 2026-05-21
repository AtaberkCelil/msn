-- Database schema for Windows Live Messenger Clone
CREATE DATABASE IF NOT EXISTS `wlm_messenger` DEFAULT CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
USE `wlm_messenger`;

-- Users table
CREATE TABLE IF NOT EXISTS `users` (
    `id` INT AUTO_INCREMENT PRIMARY KEY,
    `email` VARCHAR(255) NOT NULL UNIQUE,
    `password_hash` VARCHAR(255) NOT NULL,
    `display_name` VARCHAR(100) NOT NULL,
    `status` VARCHAR(20) NOT NULL DEFAULT 'Offline',
    `custom_sign` VARCHAR(255) NULL,
    `avatar_filename` VARCHAR(255) NULL,
    `created_at` TIMESTAMP DEFAULT CURRENT_TIMESTAMP
) ENGINE=InnoDB;

-- Contacts relationship table (friend, blocked)
CREATE TABLE IF NOT EXISTS `contacts` (
    `user_id` INT NOT NULL,
    `contact_id` INT NOT NULL,
    `relation_type` ENUM('friend', 'blocked') NOT NULL DEFAULT 'friend',
    PRIMARY KEY (`user_id`, `contact_id`),
    FOREIGN KEY (`user_id`) REFERENCES `users` (`id`) ON DELETE CASCADE,
    FOREIGN KEY (`contact_id`) REFERENCES `users` (`id`) ON DELETE CASCADE
) ENGINE=InnoDB;

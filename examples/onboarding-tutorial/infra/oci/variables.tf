# Example Pty Ltd's GRC platform on OCI (docs/examples/onboarding-tutorial.md). Every value is fictional: documentation address
# ranges, placeholder OCIDs. Give real ones in your own IaC repository, never in this public one.

variable "compartment_id" {
  type        = string
  description = "The compartment holding the GRC platform."
}

variable "availability_domain" {
  type        = string
  description = "The availability domain for the VM."
}

variable "image_id" {
  type        = string
  description = "The Ubuntu LTS image for the VM (x86)."
}

variable "region" {
  type        = string
  default     = "example-region-1"
  description = "The VM's region."
}

variable "backup_region" {
  type        = string
  default     = "example-region-2"
  description = "Where backups are kept: another region than the VM's."
}

variable "vcn_cidr" {
  type        = string
  default     = "192.0.2.0/24"
  description = "The cloud network. The tutorial uses a documentation range; use a private range that does not overlap the office."
}

variable "office_cidr" {
  type        = string
  default     = "198.51.100.0/24"
  description = "The office network, the only one that reaches the VM, over the VPN."

  validation {
    condition     = can(cidrhost(var.office_cidr, 0)) && split("/", var.office_cidr)[1] != "0"
    error_message = "office_cidr must be the office's own network, never the whole internet (0.0.0.0/0)."
  }
}

variable "xgs_public_ip" {
  type        = string
  default     = "203.0.113.10"
  description = "The office Sophos XGS firewall's public address, the VPN's customer-premises equipment."
}

variable "ssh_public_key" {
  type        = string
  default     = "ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIExampleExampleExampleExampleExampleExample admin@example.com"
  description = "The administrators' public key for Bastion sessions."
}

variable "backup_key_id" {
  type        = string
  description = "The Vault key that encrypts the backup bucket."
}

variable "backup_retention_days" {
  type        = number
  default     = 35
  description = "How long the retention rule keeps each backup. Nobody can delete or change a backup sooner."

  validation {
    condition     = var.backup_retention_days >= 35
    error_message = "backup_retention_days must be at least 35, as the backup procedure says."
  }
}

variable "backup_admins_group" {
  type        = string
  default     = "BackupAdmins"
  description = "The only group that can read the backups."
}

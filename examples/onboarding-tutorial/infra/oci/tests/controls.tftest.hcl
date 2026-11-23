# The GRC platform's hosting controls (GRC-PLATFORM), proven against the planned configuration with mock providers: no cloud
# account, no credentials and nothing created. Each run block is one control, named as hound's grc-infra-controls profile names
# it. A mock proves what the code declares; the nightly drift plan (terraform-drift) proves the live tenancy still matches the code.

mock_provider "oci" {
  mock_data "oci_objectstorage_namespace" {
    defaults = { namespace = "examplens" }
  }
  mock_data "oci_core_services" {
    defaults = {
      services = [{ id = "ocid1.service.oc1..example", cidr_block = "all-example-services-in-oracle-services-network", name = "All Example Services" }]
    }
  }
}

mock_provider "oci" {
  alias = "backup"
  mock_data "oci_objectstorage_namespace" {
    defaults = { namespace = "examplens" }
  }
}

variables {
  compartment_id      = "ocid1.compartment.oc1..example"
  availability_domain = "Example-AD-1"
  image_id            = "ocid1.image.oc1..example"
  backup_key_id       = "ocid1.key.oc1..example"
}

run "vm_has_no_public_ip" {
  command = plan
  assert {
    condition     = oci_core_instance.grc.create_vnic_details[0].assign_public_ip == "false"
    error_message = "vm_has_no_public_ip: the GRC platform's VM must not have a public IP address."
  }
  assert {
    condition     = oci_core_subnet.private.prohibit_public_ip_on_vnic == true
    error_message = "vm_has_no_public_ip: the GRC platform's subnet must refuse public IP addresses."
  }
}

run "https_only_from_the_office" {
  command = plan
  assert {
    condition = alltrue([
      for rule in oci_core_security_list.grc.ingress_security_rules :
      rule.source == var.office_cidr || rule.source == var.vcn_cidr
    ])
    error_message = "https_only_from_the_office: only the office network (and the VCN itself) may reach the VM."
  }
  assert {
    condition = alltrue([
      for rule in oci_core_security_list.grc.ingress_security_rules :
      rule.protocol == "6" && rule.tcp_options[0].min == 443 && rule.tcp_options[0].max == 443
      if rule.source == var.office_cidr
    ])
    error_message = "https_only_from_the_office: the office may reach the VM on TCP 443 only."
  }
}

run "office_range_is_not_the_internet" {
  command = plan
  variables {
    office_cidr = "0.0.0.0/0"
  }
  expect_failures = [var.office_cidr]
}

run "no_ssh_from_the_internet" {
  command = plan
  assert {
    condition = alltrue([
      for rule in oci_core_security_list.grc.ingress_security_rules :
      rule.source == var.vcn_cidr
      if rule.protocol == "6" && rule.tcp_options[0].min <= 22 && rule.tcp_options[0].max >= 22
    ])
    error_message = "no_ssh_from_the_internet: SSH may come only from inside the VCN (the Bastion's endpoint)."
  }
  assert {
    condition     = alltrue([for cidr in oci_bastion_bastion.grc.client_cidr_block_allow_list : cidr == var.office_cidr])
    error_message = "no_ssh_from_the_internet: the Bastion accepts sessions from the office network only."
  }
  assert {
    condition     = oci_bastion_bastion.grc.max_session_ttl_in_seconds <= 10800
    error_message = "no_ssh_from_the_internet: Bastion sessions last at most three hours."
  }
}

run "vpn_routes_only_the_office" {
  command = plan
  assert {
    condition     = oci_core_ipsec.office.static_routes == toset([var.office_cidr])
    error_message = "vpn_routes_only_the_office: the Site-to-Site VPN carries the office network only."
  }
  assert {
    condition     = oci_core_cpe.xgs.ip_address == var.xgs_public_ip
    error_message = "vpn_routes_only_the_office: the VPN's customer-premises equipment is the office XGS."
  }
}

run "storage_encrypted" {
  command = plan
  assert {
    condition     = oci_core_instance.grc.is_pv_encryption_in_transit_enabled == true
    error_message = "storage_encrypted: the boot volume is encrypted in transit as well as at rest."
  }
  assert {
    condition     = oci_objectstorage_bucket.backups.kms_key_id == var.backup_key_id
    error_message = "storage_encrypted: the backup bucket is encrypted with the organisation's own key."
  }
}

run "vm_metadata_v2_only" {
  command = plan
  assert {
    condition     = oci_core_instance.grc.instance_options[0].are_legacy_imds_endpoints_disabled == true
    error_message = "vm_metadata_v2_only: the VM serves instance metadata on the v2 endpoint only."
  }
}

run "backups_private_and_in_another_region" {
  command = plan
  assert {
    condition     = oci_objectstorage_bucket.backups.access_type == "NoPublicAccess"
    error_message = "backups_private_and_in_another_region: the backup bucket has no public access."
  }
  assert {
    condition     = var.backup_region != var.region
    error_message = "backups_private_and_in_another_region: backups are kept in a region other than the VM's."
  }
}

run "backups_retention_locked" {
  command = plan
  assert {
    condition = anytrue([
      for rule in oci_objectstorage_bucket.backups.retention_rules :
      rule.duration[0].time_unit == "DAYS" && rule.duration[0].time_amount >= 35
    ])
    error_message = "backups_retention_locked: a retention rule keeps every backup at least 35 days, so nobody can delete or change it sooner."
  }
}

run "backup_retention_cannot_be_shortened" {
  command = plan
  variables {
    backup_retention_days = 7
  }
  expect_failures = [var.backup_retention_days]
}

run "only_backup_admins_reach_backups" {
  command = plan
  assert {
    condition = alltrue([
      for statement in oci_identity_policy.backups.statements :
      startswith(statement, "Allow group ${var.backup_admins_group} to ")
    ])
    error_message = "only_backup_admins_reach_backups: the backup bucket's policy grants access to the backup administrators group only."
  }
  assert {
    condition     = alltrue([for statement in oci_identity_policy.backups.statements : !strcontains(statement, "manage objects")])
    error_message = "only_backup_admins_reach_backups: nobody, backup administrators included, may delete or overwrite backups (no manage objects)."
  }
}

run "volume_backed_up_weekly" {
  command = plan
  assert {
    condition     = anytrue([for s in oci_core_volume_backup_policy.weekly.schedules : s.period == "ONE_WEEK" && s.retention_seconds >= 35 * 24 * 3600])
    error_message = "volume_backed_up_weekly: the boot volume is backed up weekly and each backup kept at least 35 days."
  }
}

provider "oci" {
  region = var.region
}

provider "oci" {
  alias  = "backup"
  region = var.backup_region
}

# The network: one VCN, one private subnet with no public addresses.
resource "oci_core_vcn" "grc" {
  compartment_id = var.compartment_id
  cidr_blocks    = [var.vcn_cidr]
  display_name   = "grc-platform"
  dns_label      = "grc"
}

resource "oci_core_nat_gateway" "grc" {
  compartment_id = var.compartment_id
  vcn_id         = oci_core_vcn.grc.id
  display_name   = "grc-outbound-https"
}

data "oci_core_services" "all" {}

resource "oci_core_service_gateway" "grc" {
  compartment_id = var.compartment_id
  vcn_id         = oci_core_vcn.grc.id
  display_name   = "grc-object-storage"
  services {
    service_id = data.oci_core_services.all.services[0].id
  }
}

resource "oci_core_route_table" "private" {
  compartment_id = var.compartment_id
  vcn_id         = oci_core_vcn.grc.id
  display_name   = "grc-private"

  route_rules {
    description       = "The office, over the Site-to-Site VPN"
    destination       = var.office_cidr
    destination_type  = "CIDR_BLOCK"
    network_entity_id = oci_core_drg.grc.id
  }

  route_rules {
    description       = "Object Storage, never over the internet"
    destination       = data.oci_core_services.all.services[0].cidr_block
    destination_type  = "SERVICE_CIDR_BLOCK"
    network_entity_id = oci_core_service_gateway.grc.id
  }

  route_rules {
    description       = "Outbound HTTPS to Sophos Central and GitHub's API"
    destination       = "0.0.0.0/0"
    destination_type  = "CIDR_BLOCK"
    network_entity_id = oci_core_nat_gateway.grc.id
  }
}

resource "oci_core_security_list" "grc" {
  compartment_id = var.compartment_id
  vcn_id         = oci_core_vcn.grc.id
  display_name   = "grc-platform"

  ingress_security_rules {
    description = "Staff in the office reach CISO Assistant over HTTPS, through the VPN"
    protocol    = "6"
    source      = var.office_cidr
    tcp_options {
      min = 443
      max = 443
    }
  }

  ingress_security_rules {
    description = "SSH from the Bastion's endpoint inside the VCN only"
    protocol    = "6"
    source      = var.vcn_cidr
    tcp_options {
      min = 22
      max = 22
    }
  }

  egress_security_rules {
    description = "Outbound HTTPS through the NAT and service gateways"
    protocol    = "6"
    destination = "0.0.0.0/0"
    tcp_options {
      min = 443
      max = 443
    }
  }
}

resource "oci_core_subnet" "private" {
  compartment_id             = var.compartment_id
  vcn_id                     = oci_core_vcn.grc.id
  cidr_block                 = var.vcn_cidr
  display_name               = "grc-private"
  dns_label                  = "private"
  prohibit_public_ip_on_vnic = true
  route_table_id             = oci_core_route_table.private.id
  security_list_ids          = [oci_core_security_list.grc.id]
}

# The Site-to-Site VPN to the office XGS: a DRG, the XGS as customer-premises equipment, and an IPSec connection for the office only.
resource "oci_core_drg" "grc" {
  compartment_id = var.compartment_id
  display_name   = "grc-drg"
}

resource "oci_core_drg_attachment" "grc" {
  drg_id       = oci_core_drg.grc.id
  display_name = "grc-vcn"
  network_details {
    id   = oci_core_vcn.grc.id
    type = "VCN"
  }
}

resource "oci_core_cpe" "xgs" {
  compartment_id = var.compartment_id
  ip_address     = var.xgs_public_ip
  display_name   = "office-xgs"
}

resource "oci_core_ipsec" "office" {
  compartment_id = var.compartment_id
  cpe_id         = oci_core_cpe.xgs.id
  drg_id         = oci_core_drg.grc.id
  static_routes  = [var.office_cidr]
  display_name   = "office-site-to-site"
}

# Administrators reach the VM only through time-limited Bastion sessions from the office.
resource "oci_bastion_bastion" "grc" {
  bastion_type                 = "STANDARD"
  compartment_id               = var.compartment_id
  target_subnet_id             = oci_core_subnet.private.id
  name                         = "grcplatform"
  client_cidr_block_allow_list = [var.office_cidr]
  max_session_ttl_in_seconds   = 10800
}

# The VM: x86, private address only, metadata on the v2 endpoint only, boot volume encrypted in transit as well as at rest.
resource "oci_core_instance" "grc" {
  availability_domain                 = var.availability_domain
  compartment_id                      = var.compartment_id
  display_name                        = "grc-platform"
  shape                               = "VM.Standard.E5.Flex"
  is_pv_encryption_in_transit_enabled = true

  shape_config {
    ocpus         = 2
    memory_in_gbs = 16
  }

  create_vnic_details {
    subnet_id        = oci_core_subnet.private.id
    assign_public_ip = "false"
    private_ip       = cidrhost(var.vcn_cidr, 10)
  }

  source_details {
    source_type             = "image"
    source_id               = var.image_id
    boot_volume_size_in_gbs = 100
    boot_volume_vpus_per_gb = 10
  }

  instance_options {
    are_legacy_imds_endpoints_disabled = true
  }

  agent_config {
    plugins_config {
      name          = "Bastion"
      desired_state = "ENABLED"
    }
  }

  metadata = {
    ssh_authorized_keys = var.ssh_public_key
  }
}

# Backups: the boot volume weekly, the database nightly into a bucket in another region.
resource "oci_core_volume_backup_policy" "weekly" {
  compartment_id = var.compartment_id
  display_name   = "grc-weekly"

  schedules {
    backup_type       = "INCREMENTAL"
    period            = "ONE_WEEK"
    day_of_week       = "SUNDAY"
    hour_of_day       = 2
    offset_type       = "STRUCTURED"
    retention_seconds = var.backup_retention_days * 24 * 3600
    time_zone         = "REGIONAL_DATA_CENTER_TIME"
  }
}


resource "oci_core_volume_backup_policy_assignment" "grc" {
  asset_id  = oci_core_instance.grc.boot_volume_id
  policy_id = oci_core_volume_backup_policy.weekly.id
}

data "oci_objectstorage_namespace" "backup" {
  provider       = oci.backup
  compartment_id = var.compartment_id
}

resource "oci_objectstorage_bucket" "backups" {
  provider       = oci.backup
  compartment_id = var.compartment_id
  namespace      = data.oci_objectstorage_namespace.backup.namespace
  name           = "grc-platform-backups"
  access_type    = "NoPublicAccess"
  kms_key_id     = var.backup_key_id

  retention_rules {
    display_name = "keep-backups"
    duration {
      time_amount = var.backup_retention_days
      time_unit   = "DAYS"
    }
  }
}

# Only the backup administrators read the backups; the VM may only add new ones. The retention rule stops anyone, administrators
# included, deleting or overwriting a backup before it expires.
resource "oci_identity_dynamic_group" "grc_vm" {
  compartment_id = var.compartment_id
  name           = "grc-platform-vm"
  description    = "The GRC platform's VM, which writes the nightly database dump."
  matching_rule  = "instance.id = '${oci_core_instance.grc.id}'"
}

resource "oci_identity_policy" "backups" {
  compartment_id = var.compartment_id
  name           = "grc-platform-backups"
  description    = "Backup administrators read backups; the VM only adds them."
  statements = [
    "Allow group ${var.backup_admins_group} to read objects in compartment id ${var.compartment_id} where target.bucket.name = '${oci_objectstorage_bucket.backups.name}'",
    "Allow group ${var.backup_admins_group} to read buckets in compartment id ${var.compartment_id} where target.bucket.name = '${oci_objectstorage_bucket.backups.name}'",
    "Allow dynamic-group ${oci_identity_dynamic_group.grc_vm.name} to manage objects in compartment id ${var.compartment_id} where all {target.bucket.name = '${oci_objectstorage_bucket.backups.name}', request.permission = 'OBJECT_CREATE'}",
  ]
}

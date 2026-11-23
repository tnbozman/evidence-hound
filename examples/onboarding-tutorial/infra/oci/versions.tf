terraform {
  required_version = ">= 1.7.0"
  required_providers {
    oci = {
      source                = "oracle/oci"
      version               = "~> 9.14"
      configuration_aliases = [oci.backup]
    }
  }
}

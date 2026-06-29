#!/usr/bin/perl

use strict;
use warnings;

use Test::Deep;
use Test::More;
use Test::NoWarnings;
use Data::Dumper;

use GLPI::Agent::Tools::Solaris;
use GLPI::Agent::Task::Inventory::Solaris::Bios;

my %showrev_tests = (
    'SPARC-1' => {
        'Release' => '5.10',
        'Hostname' => '157501s021plc',
        'Kernel version' => 'SunOS',
        'Kernel architecture' => 'sun4u',
        'Hardware provider' => 'Sun_Microsystem',
        'Domain' => 'be.cnamts.fr',
        'Application architecture' => 'sparc',
        'Hostid' => '83249bbf',
    },
    'SPARC-2' => {
        'Kernel version' => 'SunOS',
        'Release' => '5.10',
        'Hostname' => 'mysunserver',
        'Hardware provider' => 'Sun_Microsystems',
        'Kernel architecture' => 'sun4v',
        'Application architecture' => 'sparc',
        'Hostid' => 'mabox'
    },
    'x86-1' => {
        'Kernel version' => 'SunOS',
        'Hostname' => 'stlaurent',
        'Kernel architecture' => 'i86pc',
        'Application architecture' => 'i386',
        'Hostid' => '403100b',
        'Release' => '5.10',
    },
    'x86-2' => {
        'Kernel version' => 'SunOS',
        'Release' => '5.10',
        'Hostname' => 'mamachine',
        'Kernel architecture' => 'i86pc',
        'Application architecture' => 'i386',
        'Hostid' => '7c31a88'
    },
    'x86-3' => {
        'Kernel version' => 'SunOS',
        'Release' => '5.10',
        'Hostname' => 'plop',
        'Kernel architecture' => 'i86pc',
        'Application architecture' => 'i386',
        'Hostid' => '7c31a36'
    }
);

my %smbios_tests = (
    'x86-1' => {
        'SMB_TYPE_SYSTEM' => {
            'Version' => '00',
            'Family' => undef,
            'SKU Number' => undef,
            'Serial Number' => 'R00T34E0009',
            'Product' => 'Sun Fire V40z',
            'Manufacturer' => 'Sun Microsystems, Inc.',
            'UUID' => 'be1630df-d130-41a4-be32-fd28bb4bd1ac',
            'Wake-Up Event' => '0x6 (power switch)'
        }
    },
    'x86-3' => {
        SMB_TYPE_CHASSIS => {
            'Chassis Height' => '1u',
            'Power Supply State' => '0x3 (safe)',
            'Element Records' => '0',
            'Serial Number' => 'QSDH1234567',
            'Thermal State' => '0x3 (safe)',
            'Lock Present' => 'N',
            'Asset Tag' => '6I012345TF',
            'Chassis Type' => '0x17 (rack mount chassis)',
            'Power Cords' => '1',
            'Version' => 'E10476-011',
            'OEM Data' => '0x81581cf8',
            'Boot-Up State' => '0x3 (safe)',
            'Manufacturer' => 'TRANSTEC'
        },
        SMB_TYPE_BIOS => {
            'Characteristics' => '0x15c099a80',
            'Version Number' => '0.0',
            'Vendor' => 'Intel Corporation',
            'Image Size' => '98304 bytes',
            'Characteristics Extension Byte 2' => '0x7',
            'Characteristics Extension Byte 1' => '0x33',
            'Address Segment' => '0xe800',
            'Version String' => 'SFC4UR.86B.01.00.0029.071220092126',
            'Embedded Ctlr Firmware Version Number' => '0.0',
            'Release Date' => '07/12/2009',
            'ROM Size' => '8388608 bytes'
        },
        SMB_TYPE_IPMIDEV => {
            'Flags' => '0x9',
            'NV Storage Device Bus ID' => '0xffffffff',
            'BMC IPMI Version' => '2.0',
            'Register Spacing' => '1',
            'Interrupt Number' => '0',
            'Type' => '1 (KCS: Keyboard Controller Style)',
            'i2c Bus Slave Address' => '0x20',
            'BMC Base Address' => '0xca2'
        },
        SMB_TYPE_BASEBOARD => {
            'Board Type' => '0xa (motherboard)',
            'Flags' => '0x9',
            'Serial Number' => 'QSFX12345678',
            'Product' => 'S7000FC4UR',
            'Manufacturer' => 'Intel',
            'Chassis' => '0',
            'Asset Tag' => '6I012345TF'
        },
        SMB_TYPE_SYSTEM => {
            'Family' => undef,
            'SKU Number' => '6I012345TF',
            'Product' => 'MP Server',
            'Manufacturer' => 'Intel',
            'UUID' => '4b713db6-6d40-11dd-b32c-000123456789',
            'Wake-Up Event' => '0x6 (power switch)'
        }
    },
    "oi-2021.10" => {
        SMB_TYPE_BIOS => {
            'Vendor' => 'innotek GmbH',
            'Image Size' => '131072 bytes',
            'Characteristics' => '0x48018090',
            'Characteristics Extension Byte 2' => '0x0',
            'Address Segment' => '0xe000',
            'Version String' => 'VirtualBox',
            'Characteristics Extension Byte 1' => '0x1',
            'ROM Size' => '131072 bytes',
            'Release Date' => '12/01/2006'
        },
        SMB_TYPE_BASEBOARD => {
            'Flags' => '0x98',
            'Board Type' => '0xa2',
            'Chassis' => '142029272'
        },
        SMB_TYPE_SYSTEM => {
            'UUID' => 'ea1635ad-bf51-864f-b673-704fa7655a01',
            'Version' => '1.2',
            'Product' => 'VirtualBox',
            'Family' => 'Virtual Machine',
            'Serial Number' => '0',
            'Wake-Up Event' => '0x6 (power switch)',
            'SKU Number' => undef,
            'Manufacturer' => 'innotek GmbH'
        },
        SMB_TYPE_OEM_LO => '0x800808002e583a00',
        SMB_TYPE_CHASSIS => {
            'Chassis Type' => '0x1 (other)',
            'Element Records' => '0',
            'OEM Data' => '0x0',
            'Power Supply State' => '0x3 (safe)',
            'Power Cords' => '0',
            'Thermal State' => '0x3 (safe)',
            'Lock Present' => 'N',
            'Chassis Height' => '0u',
            'Manufacturer' => 'Oracle Corporation',
            'Boot-Up State' => '0x3 (safe)',
            'SKU Number' => '<unknown>'
        },
        SMB_TYPE_OEMSTR => [
            'vboxVer_6.1.30',
            'vboxRev_148432'
        ]
    },
    "omnios-v11" => {
        SMB_TYPE_BASEBOARD => {
            'Asset Tag' => 'To be filled by O.E.M.',
            'Board Type' => '0xa (motherboard)',
            Chassis => 3,
            Flags => '0x9',
            'Location Tag' => 'To be filled by O.E.M.',
            Manufacturer => 'ASUSTeK COMPUTER INC.',
            Product => 'P8P67-M',
            'Serial Number' => 'MT7014018701186',
            Version => 'Rev X.0x'
        },
        SMB_TYPE_BIOS => {
            'Address Segment' => '0xf000',
            Characteristics => '0x53f8b9880',
            'Characteristics Extension Byte 1' => '0x3',
            'Characteristics Extension Byte 2' => '0x5',
            'Image Size' => '65536 bytes',
            'ROM Size' => '4194304 bytes',
            'Release Date' => '07/16/2013',
            Vendor => 'American Megatrends Inc.',
            'Version Number' => '4.6',
            'Version String' => 3703
        },
        SMB_TYPE_BOOT => {
            'Boot Data (9 bytes)' => '0x000000000000000000',
            'Boot Status Code' => '0x0 (no errors detected)'
        },
        SMB_TYPE_CACHE => [
            {
                Associativity => '7 (8-way set associative)',
                'Current SRAM Type' => '0x1 (other)',
                'Error Correction Type' => '3 (none)',
                Flags => '0x1',
                'Installed Size' => '262144 bytes',
                Level => 1,
                Location => '0 (internal)',
                'Location Tag' => 'L1-Cache',
                'Logical Cache Type' => '5 (unified)',
                'Maximum Installed Size' => '262144 bytes',
                Mode => '1 (write-back)',
                Speed => 'Unknown',
                'Supported SRAM Types' => '0x1'
            },
            {
                Associativity => '7 (8-way set associative)',
                'Current SRAM Type' => '0x1 (other)',
                'Error Correction Type' => '3 (none)',
                Flags => '0x1',
                'Installed Size' => '1048576 bytes',
                Level => 2,
                Location => '0 (internal)',
                'Location Tag' => 'L2-Cache',
                'Logical Cache Type' => '5 (unified)',
                'Maximum Installed Size' => '1048576 bytes',
                Mode => '2 (varies by address)',
                Speed => 'Unknown',
                'Supported SRAM Types' => '0x1'
            },
            {
                Associativity => '8 (16-way set associative)',
                'Current SRAM Type' => '0x1 (other)',
                'Error Correction Type' => '3 (none)',
                Flags => '0x0',
                'Installed Size' => '8388608 bytes',
                Level => 3,
                Location => '0 (internal)',
                'Location Tag' => 'L3-Cache',
                'Logical Cache Type' => '5 (unified)',
                'Maximum Installed Size' => '8388608 bytes',
                Mode => '3 (unknown)',
                Speed => 'Unknown',
                'Supported SRAM Types' => '0x1'
            }
        ],
        SMB_TYPE_CHASSIS => {
            'Asset Tag' => 'Asset-1234567890',
            'Boot-Up State' => '0x3 (safe)',
            'Chassis Height' => '0u',
            'Chassis Type' => '0x3 (desktop)',
            'Element Records' => 0,
            'Lock Present' => 'N',
            Manufacturer => 'Chassis Manufacture',
            'OEM Data' => '0x0',
            'Power Cords' => 1,
            'Power Supply State' => '0x3 (safe)',
            'SKU Number' => '<unknown>',
            'Serial Number' => 'Chassis Serial Number',
            'Thermal State' => '0x3 (safe)',
            Version => 'Chassis Version'
        },
        SMB_TYPE_COOLDEV => [
            {
                'Cooling Unit Group' => 1,
                'Device Type' => 18,
                'Nominal Speed' => 'unknown',
                'OEM- or BIOS- defined data' => '0x0',
                Status => 0,
                'Temperature Probe Handle' => 45
            },
            {
                'Cooling Unit Group' => 1,
                'Device Type' => 18,
                'Nominal Speed' => 'unknown',
                'OEM- or BIOS- defined data' => '0x0',
                Status => 0,
                'Temperature Probe Handle' => 45
            },
            {
                'Cooling Unit Group' => 1,
                'Device Type' => 18,
                'Nominal Speed' => 'unknown',
                'OEM- or BIOS- defined data' => '0x0',
                Status => 0,
                'Temperature Probe Handle' => 65
            },
            {
                'Cooling Unit Group' => 1,
                'Device Type' => 18,
                'Nominal Speed' => 'unknown',
                'OEM- or BIOS- defined data' => '0x0',
                Status => 0,
                'Temperature Probe Handle' => 71
            }
        ],
        SMB_TYPE_IPROBE => [
            {
                Description => 'ABC',
                Location => 0,
                'Maximum Possible Current' => 'unknown',
                'Minimum Possible Current' => 'unknown',
                'OEM- or BIOS- defined value' => '0x0',
                'Probe Accuracy' => 'unknown',
                'Probe Nominal Value' => 'unknown',
                'Probe Resolution' => 'unknown',
                'Probe Tolerance' => 'unknown',
                Status => 0
            },
            {
                Description => 'DEF',
                Location => 0,
                'Maximum Possible Current' => 'unknown',
                'Minimum Possible Current' => 'unknown',
                'OEM- or BIOS- defined value' => '0x0',
                'Probe Accuracy' => 'unknown',
                'Probe Nominal Value' => 'unknown',
                'Probe Resolution' => 'unknown',
                'Probe Tolerance' => 'unknown',
                Status => 0
            },
            {
                Description => 'GHI',
                Location => 0,
                'Maximum Possible Current' => 'unknown',
                'Minimum Possible Current' => 'unknown',
                'OEM- or BIOS- defined value' => '0x0',
                'Probe Accuracy' => 'unknown',
                'Probe Nominal Value' => 'unknown',
                'Probe Resolution' => 'unknown',
                'Probe Tolerance' => 'unknown',
                Status => 0
            }
        ],
        SMB_TYPE_LANG => {
            'Current Language' => 'en-',
            'Installed Languages' => [
                'en-',
                'fr-',
                'de-',
                'ja-',
                'zh-',
                'chs'
            ],
            'Language String Format' => 1,
            'Number of Installed Languages' => 6
        },
        SMB_TYPE_MEMARRAY => {
            ECC => '3 (none)',
            Location => '3 (system board or motherboard)',
            'Max Capacity' => '34359738368 bytes',
            'Memory Error Data' => 91,
            'Number of Slots/Sockets' => 4,
            Use => '3 (system memory)'
        },
        SMB_TYPE_MEMARRAYMAP => {
            'Devices per Row' => 4,
            'Physical Address' => '0x0',
            'Physical Memory Array' => 89,
            Size => '25769803776 bytes'
        },
        SMB_TYPE_MEMDEVICE => [
            {
                'Asset Tag' => '9876543210',
                'Bank Locator' => 'BANK 0',
                'Configured Speed' => 'Unknown',
                'Configured Voltage' => 'Unknown',
                'Data Width' => '64 bits',
                'Device Locator' => 'ChannelA-DIMM0',
                Flags => '0x80',
                'Form Factor' => '9 (DIMM)',
                'Location Tag' => 'ChannelA-DIMM0',
                Manufacturer => '8C26',
                'Maximum Voltage' => 'Unknown',
                'Memory Error Data' => 92,
                'Memory Type' => '24 (DDR3)',
                'Minimum Voltage' => 'Unknown',
                'Part Number' => 'TIMETEC-UD3-1333',
                'Physical Memory Array' => 89,
                Rank => '2 (dual)',
                'Serial Number' => '00000367',
                Set => 'None',
                Size => '8589934592 bytes',
                Speed => '1333 MT/s',
                'Total Width' => '64 bits'
            },
            {
                'Asset Tag' => '9876543210',
                'Bank Locator' => 'BANK 1',
                'Configured Speed' => 'Unknown',
                'Configured Voltage' => 'Unknown',
                'Data Width' => '64 bits',
                'Device Locator' => 'ChannelA-DIMM1',
                Flags => '0x80',
                'Form Factor' => '9 (DIMM)',
                'Location Tag' => 'ChannelA-DIMM1',
                Manufacturer => 8502,
                'Maximum Voltage' => 'Unknown',
                'Memory Error Data' => 'None',
                'Memory Type' => '24 (DDR3)',
                'Minimum Voltage' => 'Unknown',
                'Part Number' => '1600EL Series',
                'Physical Memory Array' => 89,
                Rank => '2 (dual)',
                'Serial Number' => '00000000',
                Set => 'None',
                Size => '4294967296 bytes',
                Speed => '1333 MT/s',
                'Total Width' => '64 bits'
            },
            {
                'Asset Tag' => '9876543210',
                'Bank Locator' => 'BANK 2',
                'Configured Speed' => 'Unknown',
                'Configured Voltage' => 'Unknown',
                'Data Width' => '64 bits',
                'Device Locator' => 'ChannelB-DIMM0',
                Flags => '0x80',
                'Form Factor' => '9 (DIMM)',
                'Location Tag' => 'ChannelB-DIMM0',
                Manufacturer => '8C26',
                'Maximum Voltage' => 'Unknown',
                'Memory Error Data' => 97,
                'Memory Type' => '24 (DDR3)',
                'Minimum Voltage' => 'Unknown',
                'Part Number' => 'TIMETEC-UD3-1333',
                'Physical Memory Array' => 89,
                Rank => '2 (dual)',
                'Serial Number' => '00000368',
                Set => 'None',
                Size => '8589934592 bytes',
                Speed => '1333 MT/s',
                'Total Width' => '64 bits'
            },
            {
                'Asset Tag' => '9876543210',
                'Bank Locator' => 'BANK 3',
                'Configured Speed' => 'Unknown',
                'Configured Voltage' => 'Unknown',
                'Data Width' => '64 bits',
                'Device Locator' => 'ChannelB-DIMM1',
                Flags => '0x80',
                'Form Factor' => '9 (DIMM)',
                'Location Tag' => 'ChannelB-DIMM1',
                Manufacturer => 8502,
                'Maximum Voltage' => 'Unknown',
                'Memory Error Data' => 'None',
                'Memory Type' => '24 (DDR3)',
                'Minimum Voltage' => 'Unknown',
                'Part Number' => '1600EL Series',
                'Physical Memory Array' => 89,
                Rank => '2 (dual)',
                'Serial Number' => '00000000',
                Set => 'None',
                Size => '4294967296 bytes',
                Speed => '1333 MT/s',
                'Total Width' => '64 bits'
            }
        ],
        SMB_TYPE_MEMDEVICEMAP => [
            {
                'Interleave Data Depth' => 2,
                'Interleave Position' => 1,
                'Memory Array Mapped Address' => 100,
                'Memory Device' => 88,
                'Partition Row Position' => 255,
                'Physical Address' => '0x0',
                Size => '8589934592 bytes'
            },
            {
                'Interleave Data Depth' => 2,
                'Interleave Position' => 1,
                'Memory Array Mapped Address' => 100,
                'Memory Device' => 93,
                'Partition Row Position' => 255,
                'Physical Address' => '0x400000000',
                Size => '4294967296 bytes'
            },
            {
                'Interleave Data Depth' => 2,
                'Interleave Position' => 2,
                'Memory Array Mapped Address' => 100,
                'Memory Device' => 94,
                'Partition Row Position' => 255,
                'Physical Address' => '0x200000000',
                Size => '8589934592 bytes'
            },
            {
                'Interleave Data Depth' => 2,
                'Interleave Position' => 2,
                'Memory Array Mapped Address' => 100,
                'Memory Device' => 99,
                'Partition Row Position' => 255,
                'Physical Address' => '0x500000000',
                Size => '4294967296 bytes'
            }
        ],
        SMB_TYPE_MEMERR32 => '0x1217620003020200000000000000800000008000000080',
        SMB_TYPE_MGMTDEV => '0x22103a00010400000000034c4d37382d32',
        SMB_TYPE_MGMTDEVCP => '0x230b5200013a004f004a00546f2042652046696c6c6564204279204f2e452e4d2e',
        SMB_TYPE_MGMTDEVDATA => '0x24105100008000800080008000800080',
        SMB_TYPE_OBDEVEXT => [
            {
                'Bus Number' => 0,
                'Device Enabled' => 'true',
                'Device Type' => 'video',
                'Device Type Instance' => 1,
                'Device/Function Number' => 16,
                'Reference Designator' => 'Onboard IGD',
                'Segment Group Number' => 0
            },
            {
                'Bus Number' => 0,
                'Device Enabled' => 'true',
                'Device Type' => 'Ethernet',
                'Device Type Instance' => 1,
                'Device/Function Number' => 200,
                'Reference Designator' => 'Onboard LAN',
                'Segment Group Number' => 0
            },
            {
                'Bus Number' => 3,
                'Device Enabled' => 'true',
                'Device Type' => 'Other',
                'Device Type Instance' => 1,
                'Device/Function Number' => 226,
                'Reference Designator' => 'Onboard 1394',
                'Segment Group Number' => 0
            }
        ],
        SMB_TYPE_OBDEVS => 'Onboard Ethernet',
        SMB_TYPE_OEMSTR => [
            'F46D04D683EF',
            'To Be Filled By O.E.M.',
            'To Be Filled By O.E.M.',
            'To Be Filled By O.E.M.'
        ],
        'SMB_TYPE_OEM_LO+11' => '0x8b365700001e8c000042f34604043255f800a202a10040634310fe8103df40b2002000733c1008000000000000000000000000000001563133393447554944',
        SMB_TYPE_PORT => [
            {
                'External Connector Type' => '15 (PS/2)',
                'External Reference Designator' => 'PS/2 Keyboard',
                'Internal Connector Type' => '0 (none)',
                'Internal Reference Designator' => 'PS/2 Keyboard',
                'Location Tag' => 'PS/2 Keyboard',
                'Port Type' => '13 (Keyboard port)'
            },
            {
                'External Connector Type' => '15 (PS/2)',
                'External Reference Designator' => 'PS/2 Mouse',
                'Internal Connector Type' => '0 (none)',
                'Internal Reference Designator' => 'PS/2 Mouse',
                'Location Tag' => 'PS/2 Mouse',
                'Port Type' => '13 (Keyboard port)'
            },
            {
                'External Connector Type' => '18 (USB)',
                'External Reference Designator' => 'USB7_8',
                'Internal Connector Type' => '0 (none)',
                'Internal Reference Designator' => 'USB7_8',
                'Location Tag' => 'USB7_8',
                'Port Type' => '16 (USB)'
            },
            {
                'External Connector Type' => '18 (USB)',
                'External Reference Designator' => 'USB6_5',
                'Internal Connector Type' => '0 (none)',
                'Internal Reference Designator' => 'USB6_5',
                'Location Tag' => 'USB6_5',
                'Port Type' => '16 (USB)'
            },
            {
                'External Connector Type' => '18 (USB)',
                'External Reference Designator' => 'USB4_3',
                'Internal Connector Type' => '0 (none)',
                'Internal Reference Designator' => 'USB4_3',
                'Location Tag' => 'USB4_3',
                'Port Type' => '16 (USB)'
            },
            {
                'External Connector Type' => '18 (USB)',
                'External Reference Designator' => 'USB1_2',
                'Internal Connector Type' => '0 (none)',
                'Internal Reference Designator' => 'USB1_2',
                'Location Tag' => 'USB1_2',
                'Port Type' => '16 (USB)'
            },
            {
                'External Connector Type' => '255 (other)',
                'External Reference Designator' => 'SPDIF_O2',
                'Internal Connector Type' => '0 (none)',
                'Internal Reference Designator' => 'SPDIF_O2',
                'Location Tag' => 'SPDIF_O2',
                'Port Type' => '255 (other)'
            },
            {
                'External Connector Type' => '11 (RJ-45)',
                'External Reference Designator' => 'GbE LAN',
                'Internal Connector Type' => '0 (none)',
                'Internal Reference Designator' => 'GbE LAN',
                'Location Tag' => 'GbE LAN',
                'Port Type' => '31 (Network port)'
            },
            {
                'External Connector Type' => '255 (other)',
                'External Reference Designator' => 'AUDIO',
                'Internal Connector Type' => '0 (none)',
                'Internal Reference Designator' => 'AUDIO',
                'Location Tag' => 'AUDIO',
                'Port Type' => '29 (Audio port)'
            },
            {
                'External Connector Type' => '0 (none)',
                'External Reference Designator' => undef,
                'Internal Connector Type' => '34 (SAS/SATA plug receptacle)',
                'Internal Reference Designator' => 'SATA3G_1',
                'Location Tag' => 'SATA3G_1',
                'Port Type' => '32 (SATA)'
            },
            {
                'External Connector Type' => '0 (none)',
                'External Reference Designator' => undef,
                'Internal Connector Type' => '34 (SAS/SATA plug receptacle)',
                'Internal Reference Designator' => 'SATA3G_2',
                'Location Tag' => 'SATA3G_2',
                'Port Type' => '32 (SATA)'
            },
            {
                'External Connector Type' => '0 (none)',
                'External Reference Designator' => undef,
                'Internal Connector Type' => '34 (SAS/SATA plug receptacle)',
                'Internal Reference Designator' => 'SATA3G_3',
                'Location Tag' => 'SATA3G_3',
                'Port Type' => '32 (SATA)'
            },
            {
                'External Connector Type' => '0 (none)',
                'External Reference Designator' => undef,
                'Internal Connector Type' => '34 (SAS/SATA plug receptacle)',
                'Internal Reference Designator' => 'SATA3G_4',
                'Location Tag' => 'SATA3G_4',
                'Port Type' => '32 (SATA)'
            },
            {
                'External Connector Type' => '0 (none)',
                'External Reference Designator' => undef,
                'Internal Connector Type' => '34 (SAS/SATA plug receptacle)',
                'Internal Reference Designator' => 'SATA6G_1',
                'Location Tag' => 'SATA6G_1',
                'Port Type' => '32 (SATA)'
            },
            {
                'External Connector Type' => '0 (none)',
                'External Reference Designator' => undef,
                'Internal Connector Type' => '34 (SAS/SATA plug receptacle)',
                'Internal Reference Designator' => 'SATA6G_2',
                'Location Tag' => 'SATA6G_2',
                'Port Type' => '32 (SATA)'
            },
            {
                'External Connector Type' => '0 (none)',
                'External Reference Designator' => undef,
                'Internal Connector Type' => '18 (USB)',
                'Internal Reference Designator' => 'USB9_10',
                'Location Tag' => 'USB9_10',
                'Port Type' => '16 (USB)'
            },
            {
                'External Connector Type' => '0 (none)',
                'External Reference Designator' => undef,
                'Internal Connector Type' => '18 (USB)',
                'Internal Reference Designator' => 'USB11_12',
                'Location Tag' => 'USB11_12',
                'Port Type' => '16 (USB)'
            },
            {
                'External Connector Type' => '0 (none)',
                'External Reference Designator' => undef,
                'Internal Connector Type' => '18 (USB)',
                'Internal Reference Designator' => 'USB13_14',
                'Location Tag' => 'USB13_14',
                'Port Type' => '16 (USB)'
            },
            {
                'External Connector Type' => '0 (none)',
                'External Reference Designator' => undef,
                'Internal Connector Type' => '8 (DB-9 pin male)',
                'Internal Reference Designator' => 'COM1',
                'Location Tag' => 'COM1',
                'Port Type' => '9 (Serial Port 16550A compat)'
            },
            {
                'External Connector Type' => '0 (none)',
                'External Reference Designator' => undef,
                'Internal Connector Type' => '8 (DB-9 pin male)',
                'Internal Reference Designator' => 'LPT',
                'Location Tag' => 'LPT',
                'Port Type' => '9 (Serial Port 16550A compat)'
            },
            {
                'External Connector Type' => '0 (none)',
                'External Reference Designator' => undef,
                'Internal Connector Type' => '31 (Mini-jack (headphones))',
                'Internal Reference Designator' => 'AAFP',
                'Location Tag' => 'AAFP',
                'Port Type' => '29 (Audio port)'
            },
            {
                'External Connector Type' => '0 (none)',
                'External Reference Designator' => undef,
                'Internal Connector Type' => '255 (other)',
                'Internal Reference Designator' => 'CPU_FAN',
                'Location Tag' => 'CPU_FAN',
                'Port Type' => '255 (other)'
            },
            {
                'External Connector Type' => '0 (none)',
                'External Reference Designator' => undef,
                'Internal Connector Type' => '255 (other)',
                'Internal Reference Designator' => 'CHA_FAN1',
                'Location Tag' => 'CHA_FAN1',
                'Port Type' => '255 (other)'
            },
            {
                'External Connector Type' => '0 (none)',
                'External Reference Designator' => undef,
                'Internal Connector Type' => '255 (other)',
                'Internal Reference Designator' => 'PWR_FAN',
                'Location Tag' => 'PWR_FAN',
                'Port Type' => '255 (other)'
            },
            {
                'External Connector Type' => '0 (none)',
                'External Reference Designator' => undef,
                'Internal Connector Type' => '22 (on-board IDE)',
                'Internal Reference Designator' => 'PATA_IDE',
                'Location Tag' => 'PATA_IDE',
                'Port Type' => '255 (other)'
            }
        ],
        SMB_TYPE_POWERSUP => [
            {
                'Asset Tag' => 'To Be Filled By O.E.M.',
                Characteristics => '0x0',
                'Cooling Device Handle' => 48,
                'Current Probe Handle' => 54,
                'Input Voltage Range Switching' => 0,
                'Location Tag' => 'To Be Filled By O.E.M.',
                Manufacturer => 'To Be Filled By O.E.M.',
                'Maximum Output' => 'unknown',
                'Part Number' => 'To Be Filled By O.E.M.',
                'Power Supply Group' => 1,
                Product => 'To Be Filled By O.E.M.',
                'Serial Number' => 'To Be Filled By O.E.M.',
                Status => 0,
                Type => 0,
                Version => 'To Be Filled By O.E.M.',
                'Voltage Probe Handle' => 42
            },
            {
                'Asset Tag' => 'To Be Filled By O.E.M.',
                Characteristics => '0x0',
                'Cooling Device Handle' => 48,
                'Current Probe Handle' => 54,
                'Input Voltage Range Switching' => 0,
                'Location Tag' => 'To Be Filled By O.E.M.',
                Manufacturer => 'To Be Filled By O.E.M.',
                'Maximum Output' => 'unknown',
                'Part Number' => 'To Be Filled By O.E.M.',
                'Power Supply Group' => 1,
                Product => 'To Be Filled By O.E.M.',
                'Serial Number' => 'To Be Filled By O.E.M.',
                Status => 0,
                Type => 0,
                Version => 'To Be Filled By O.E.M.',
                'Voltage Probe Handle' => 42
            }
        ],
        SMB_TYPE_PROCESSOR => {
            'Asset Tag' => 'To Be Filled By O.E.M.',
            CPUID => '0xbfebfbff000206a7',
            'Core Count' => 4,
            'Cores Enabled' => 1,
            'Current Speed' => '3400MHz',
            'External Clock Speed' => '100MHz',
            Family => '191 (Intel Core 2 Duo)',
            'L1 Cache Handle' => 5,
            'L2 Cache Handle' => 6,
            'L3 Cache Handle' => 7,
            'Location Tag' => 'LGA1155',
            Manufacturer => 'Intel',
            'Maximum Speed' => '3800MHz',
            'Part Number' => 'To Be Filled By O.E.M.',
            'Processor Characteristics' => '0x4',
            'Processor Status' => '1 (enabled)',
            'Serial Number' => 'To Be Filled By O.E.M.',
            'Socket Status' => 'Populated',
            'Socket Upgrade' => '1 (other)',
            'Supported Voltages' => '1.0V',
            'Thread Count' => 2,
            'Threads Enabled' => 'Unknown',
            Type => '3 (central processor)',
            Version => 'Intel(R) Core(TM) i7-2600K CPU @ 3.40GHz'
        },
        SMB_TYPE_SLOT => [
            {
                'Bus Number' => 1,
                'Device/Function Number' => '1/0',
                Height => 'unknown',
                Length => '0x3 (short length)',
                'Location Tag' => 'PCIEX16_1',
                'Reference Designator' => 'PCIEX16_1',
                'Segment Group' => 0,
                'Slot Characteristics 1' => '0xc',
                'Slot Characteristics 2' => '0x1',
                'Slot ID' => '0x1',
                Type => '0xa5 (PCI Express)',
                Usage => '0x4 (in use)',
                Width => '0x5 (32 bit)'
            },
            {
                'Bus Number' => 255,
                'Device/Function Number' => '28/3',
                Height => 'unknown',
                Length => '0x3 (short length)',
                'Location Tag' => 'PCIEX1_1',
                'Reference Designator' => 'PCIEX1_1',
                'Segment Group' => 0,
                'Slot Characteristics 1' => '0xc',
                'Slot Characteristics 2' => '0x1',
                'Slot ID' => '0x2',
                Type => '0xa5 (PCI Express)',
                Usage => '0x3 (available)',
                Width => '0x5 (32 bit)'
            },
            {
                'Bus Number' => 255,
                'Device/Function Number' => '28/4',
                Height => 'unknown',
                Length => '0x3 (short length)',
                'Location Tag' => 'PCIEX1_2',
                'Reference Designator' => 'PCIEX1_2',
                'Segment Group' => 0,
                'Slot Characteristics 1' => '0xc',
                'Slot Characteristics 2' => '0x1',
                'Slot ID' => '0x3',
                Type => '0xa5 (PCI Express)',
                Usage => '0x3 (available)',
                Width => '0x5 (32 bit)'
            },
            {
                'Bus Number' => 4,
                'Device/Function Number' => '28/6',
                Height => 'unknown',
                Length => '0x3 (short length)',
                'Location Tag' => 'PCI1',
                'Reference Designator' => 'PCI1',
                'Segment Group' => 0,
                'Slot Characteristics 1' => '0xc',
                'Slot Characteristics 2' => '0x1',
                'Slot ID' => '0x4',
                Type => '0x6 (PCI)',
                Usage => '0x3 (available)',
                Width => '0x5 (32 bit)'
            }
        ],
        SMB_TYPE_SYSCONFSTR => 'To Be Filled By O.E.M.',
        SMB_TYPE_SYSTEM => {
            Family => 'To be filled by O.E.M.',
            Manufacturer => 'System manufacturer',
            Product => 'System Product Name',
            'SKU Number' => 'SKU',
            'Serial Number' => 'System Serial Number',
            UUID => '208f001e-8c00-0042-f346-f46d04d683ef',
            Version => 'System Version',
            'Wake-Up Event' => '0x6 (power switch)'
        },
        SMB_TYPE_TPROBE => [
            {
                Description => 'LM78A',
                Location => 0,
                'Maximum Possible Temperature' => 'unknown',
                'Minimum Possible Temperature' => 'unknown',
                'OEM- or BIOS- defined value' => '0x0',
                'Probe Accuracy' => 'unknown',
                'Probe Nominal Value' => 'unknown',
                'Probe Resolution' => 'unknown',
                'Probe Tolerance' => 'unknown',
                Status => 0
            },
            {
                Description => 'LM78B',
                Location => 0,
                'Maximum Possible Temperature' => 'unknown',
                'Minimum Possible Temperature' => 'unknown',
                'OEM- or BIOS- defined value' => '0x0',
                'Probe Accuracy' => 'unknown',
                'Probe Nominal Value' => 'unknown',
                'Probe Resolution' => 'unknown',
                'Probe Tolerance' => 'unknown',
                Status => 0
            },
            {
                Description => 'LM78B',
                Location => 0,
                'Maximum Possible Temperature' => 'unknown',
                'Minimum Possible Temperature' => 'unknown',
                'OEM- or BIOS- defined value' => '0x0',
                'Probe Accuracy' => 'unknown',
                'Probe Nominal Value' => 'unknown',
                'Probe Resolution' => 'unknown',
                'Probe Tolerance' => 'unknown',
                Status => 0
            }
        ],
        SMB_TYPE_VPROBE => [
            {
                Description => 'LM78A',
                Location => 0,
                'Maximum Possible Voltage' => 'unknown',
                'Minimum Possible Voltage' => 'unknown',
                'OEM- or BIOS- defined value' => '0x0',
                'Probe Accuracy' => 'unknown',
                'Probe Nominal Value' => 'unknown',
                'Probe Resolution' => 'unknown',
                'Probe Tolerance' => 'unknown',
                Status => 0
            },
            {
                Description => 'LM78B',
                Location => 0,
                'Maximum Possible Voltage' => 'unknown',
                'Minimum Possible Voltage' => 'unknown',
                'OEM- or BIOS- defined value' => '0x0',
                'Probe Accuracy' => 'unknown',
                'Probe Nominal Value' => 'unknown',
                'Probe Resolution' => 'unknown',
                'Probe Tolerance' => 'unknown',
                Status => 0
            },
            {
                Description => 'LM78B',
                Location => 0,
                'Maximum Possible Voltage' => 'unknown',
                'Minimum Possible Voltage' => 'unknown',
                'OEM- or BIOS- defined value' => '0x0',
                'Probe Accuracy' => 'unknown',
                'Probe Nominal Value' => 'unknown',
                'Probe Resolution' => 'unknown',
                'Probe Tolerance' => 'unknown',
                Status => 0
            }
        ]
    }
);

plan tests =>
    (scalar keys %showrev_tests) +
    (scalar keys %smbios_tests)  +
    1;

foreach my $test (keys %showrev_tests) {
    my $file   = "resources/solaris/showrev/$test";
    my $result = GLPI::Agent::Task::Inventory::Solaris::Bios::_parseShowRev(file => $file);
    cmp_deeply($result, $showrev_tests{$test}, "showrev parsing: $test");
}

foreach my $test (keys %smbios_tests) {
    my $file   = "resources/solaris/smbios/$test";
    my $result = GLPI::Agent::Tools::Solaris::getSmbios(file => $file);
    if (ref($smbios_tests{$test}) eq 'HASH' && keys(%{$smbios_tests{$test}})) {
        cmp_deeply($result, $smbios_tests{$test}, "smbios parsing: $test");
    } else {
        my $dumper = Data::Dumper->new([$result], [$test])->Useperl(1)->Indent(1)->Quotekeys(0)->Sortkeys(1)->Pad("    ");
        $dumper->{xpad} = "    ";
        print STDERR $dumper->Dump();
        fail "$test: result still not integrated";
    }
}

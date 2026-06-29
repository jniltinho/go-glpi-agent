package GLPI::Agent::SNMP::MibSupport::Quantum;

use strict;
use warnings;

use parent 'GLPI::Agent::SNMP::MibSupportTemplate';

use GLPI::Agent::Tools;
use GLPI::Agent::Tools::SNMP;

use constant    enterprises => '.1.3.6.1.4.1' ;

use constant    quantum  => enterprises . '.3764';

# ADIC-INTELLIGENT-STORAGE-MIB
use constant    productAgentInfo    => quantum . '.1.1.10';
use constant    components          => quantum . '.1.1.30';

use constant    productMibVersion       => productAgentInfo . '.1.0';
use constant    productSnmpAgentVersion => productAgentInfo . '.2.0';
use constant    productName             => productAgentInfo . '.3.0';
use constant    productVendor           => productAgentInfo . '.6.0';
use constant    productSerialNumber     => productAgentInfo . '.10.0';

use constant    componentType               => components . '.10.1.2';
use constant    componentDisplayName        => components . '.10.1.3';
use constant    componentSn                 => components . '.10.1.7';
use constant    componentFirmwareVersion    => components . '.10.1.11';
use constant    componentIpAddress          => components . '.10.1.17';

our $mibSupport = [
    {
        name        => "quantum",
        sysobjectid => getRegexpOidMatch(quantum)
    }
];

sub getFirmware {
    my ($self) = @_;

    return getCanonicalString($self->get(productSnmpAgentVersion));
}

sub _index {
    my ($key) = @_;
    my ($index) = $key =~ /(\d+)$/
        or return 0;
    return int($index)
}

sub getComponents {
    my ($self) = @_;

    my $device = $self->device
        or return;

    my %types = (
        1, "mcb",       # Management control blade
        2, "cmb",       # Control management blade
        3, "ioblade",   # Fibre Channel I/O blade
        4, "rcu",       # Robotics control unit
        5, "chassis",   # Network chassis
        6, "control",   # Control module
        7, "expansion", # Expansion module
        8, "psu",       # PowerSupply
    );

    my $types = $self->walk(componentType);
    my $names = $self->walk(componentDisplayName);
    my $SNs   = $self->walk(componentSn);
    my $FWs   = $self->walk(componentFirmwareVersion);
    my $IPs   = $self->walk(componentIpAddress);

    my @components;

    foreach my $key (sort { _index($a) <=> _index($b) } keys(%{$types})) {
        my $name = trimWhitespace(getCanonicalString($names->{$key}));
        my $serial = trimWhitespace(getCanonicalString($SNs->{$key} // ""));
        my $firmwareversion = trimWhitespace(getCanonicalString($FWs->{$key} // ""));
        my $type = $types{$types->{$key}} // "unknown";
        push @components, {
            CONTAINEDININDEX => 0,
            INDEX            => _index($key),
            NAME             => $name,
            TYPE             => $type,
            SERIAL           => $serial,
            FIRMWARE         => $firmwareversion,
            IP               => trimWhitespace(getCanonicalString($IPs->{$key} // "")),
        };

        if ($firmwareversion) {
            my $firmware = {
                NAME            => $name,
                DESCRIPTION     => $name." version",
                TYPE            => $type,
                VERSION         => $firmwareversion,
                MANUFACTURER    => $device->{MANUFACTURER}
            };
            $device->addFirmware($firmware);
        }
    }

    # adding library unit
    if (scalar @components) {
        unshift @components, {
            CONTAINEDININDEX => -1,
            INDEX            => 0,
            TYPE             => 'storage library',
            NAME             => $device->{MANUFACTURER}." ".$device->{MODEL}
        };
    }

    return \@components;
}

sub getManufacturer {
    my ($self) = @_;

    return trimWhitespace(getCanonicalString($self->get(productVendor)));
}

sub getModel {
    my ($self) = @_;

    return getCanonicalString($self->get(productName));
}

sub getSerial {
    my ($self) = @_;

    return getCanonicalString($self->get(productSerialNumber));
}

sub getType {
    return "STORAGE";
}

sub run {
    my ($self) = @_;

    my $device = $self->device
        or return;

    my $productMibVersion = getCanonicalString($self->get(productMibVersion));
    if ($productMibVersion) {
        my $firmware = {
            NAME            => "$device->{MODEL} MIB",
            DESCRIPTION     => "$device->{MODEL} MIB version",
            TYPE            => "mib",
            VERSION         => $productMibVersion,
            MANUFACTURER    => $device->{MANUFACTURER}
        };
        $device->addFirmware($firmware);
    }
}

1;

__END__

=head1 NAME

GLPI::Agent::SNMP::MibSupport::Quantum - Inventory module for Quantum devices

=head1 DESCRIPTION

The module enhances Quantum devices support.

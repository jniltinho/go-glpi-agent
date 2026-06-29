package GLPI::Agent::SNMP::MibSupport::DigiPower;

use strict;
use warnings;

use parent 'GLPI::Agent::SNMP::MibSupportTemplate';

use GLPI::Agent::Tools;
use GLPI::Agent::Tools::SNMP;

# DigiPower-PDU-MIB
use constant enterprises    => '.1.3.6.1.4.1';
use constant digipower      => enterprises . '.17420';

use constant devMAC     => digipower . '.1.2.3.0';
use constant devVersion => digipower . '.1.2.4.0';

use constant pdu01ModelNo   => digipower . '.1.2.9.1.19.0';

our $mibSupport = [
    {
        name        => "digipower",
        sysobjectid => getRegexpOidMatch(digipower)
    }
];

sub getFirmware {
    my ($self) = @_;

    return getCanonicalString($self->get(devVersion));
}

sub getMacAddress {
    my ($self) = @_;

    my $device = $self->device
        or return;

    return if $device->{MAC};

    return getCanonicalMacAddress($self->get(devMAC));
}

sub getModel {
    my ($self) = @_;

    return getCanonicalString($self->get(pdu01ModelNo));
}

sub getType {
    return 'NETWORKING';
}

1;

__END__

=head1 NAME

GLPI::Agent::SNMP::MibSupport::DigiPower - Inventory module for Digipower devices

=head1 DESCRIPTION

The module adds support for Digipower devices

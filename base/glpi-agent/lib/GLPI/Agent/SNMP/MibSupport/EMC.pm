package GLPI::Agent::SNMP::MibSupport::EMC;

use strict;
use warnings;

use parent 'GLPI::Agent::SNMP::MibSupportTemplate';

use GLPI::Agent::Tools;
use GLPI::Agent::Tools::SNMP;

use constant    emc => '.1.3.6.1.4.1.674';

# FCMGMT-MIB
use constant    experimental    => '.1.3.6.1.3';
use constant    fcmgmt          => experimental .'.94';

use constant    connUnitTable   => fcmgmt . '.1.6';
use constant    connUnitEntry   => connUnitTable . '.1';
use constant    connUnitId      => connUnitEntry . '.1';
use constant    connUnitProduct => connUnitEntry . '.7';
use constant    connUnitSn      => connUnitEntry . '.8';

our $mibSupport = [
    {
        name        => "emc",
        sysobjectid => getRegexpOidMatch(emc)
    }
];

sub getType {
    my ($self) = @_;

    # Only set type if we match storage experimental OID, we don't want to reset
    # Dell printers type by mistake
    my $connUnitId = $self->walk(connUnitId)
        or return;

    return 'NETWORKING';
}

sub getSerial {
    my ($self) = @_;

    my $connUnitId = $self->walk(connUnitId)
        or return;

    my ($unitId) = sort keys(%{$connUnitId})
        or return;

    return getCanonicalSerialNumber($self->get(connUnitSn . ".$unitId"));
}

sub getModel {
    my ($self) = @_;

    my $connUnitId = $self->walk(connUnitId)
        or return;

    my ($unitId) = sort keys(%{$connUnitId})
        or return;

    return getCanonicalString($self->get(connUnitProduct . ".$unitId"));
}

1;

__END__

=head1 NAME

GLPI::Agent::SNMP::MibSupport::EMC - Inventory module for EMC devices

=head1 DESCRIPTION

The module enhances EMC devices support.

package GLPI::Agent::SNMP::MibSupport::Netgear;

use strict;
use warnings;

use parent 'GLPI::Agent::SNMP::MibSupportTemplate';

use GLPI::Agent::Tools;
use GLPI::Agent::Tools::SNMP;

use constant netgear    => '.1.3.6.1.4.1.4526';

# NETGEAR-INVENTORY-MIB
use constant fastPathInventory          => netgear . '.10.13';
use constant AgentInventoryUnitEntry    => fastPathInventory . '.2.2.1';

use constant agentInventoryUnitStatus       => AgentInventoryUnitEntry . '.11';
use constant agentInventoryUnitSerialNumber => AgentInventoryUnitEntry . '.19';

# NG700-INVENTORY-MIB
use constant fastPathInventory2         => netgear . '.11.13';
use constant AgentInventoryUnitEntry2   => fastPathInventory2 . '.2.2.1';
use constant agentInventoryUnitStatus2       => AgentInventoryUnitEntry2 . '.11';
use constant agentInventoryUnitSerialNumber2 => AgentInventoryUnitEntry2 . '.19';

our $mibSupport = [
    {
        name    => "netgear-ng7000",
        oid     => fastPathInventory,
    },
    {
        name    => "netgear-ng700",
        oid     => fastPathInventory2,
    }
];

sub run {
    my ($self) = @_;

    my $device = $self->device
        or return;

    return unless ref($device->{COMPONENTS}) eq 'HASH' && ref($device->{COMPONENTS}->{COMPONENT}) eq 'ARRAY';

    # In the case we have more than one chassis component, we have to fix component serials
    my @chassis = grep { $_->{TYPE} && $_->{TYPE} eq 'chassis' } @{$device->{COMPONENTS}->{COMPONENT}};
    return unless @chassis > 1;

    my $status = $self->walk(agentInventoryUnitStatus) // $self->walk(agentInventoryUnitStatus2);
    my $serial = $self->walk(agentInventoryUnitSerialNumber) // $self->walk(agentInventoryUnitSerialNumber2);

    foreach my $chassis (@chassis) {
        next unless $chassis->{NAME} && $chassis->{NAME} =~ /^Unit (\d+)$/;
        my $unit = $1;

        # Only check for available chassis
        next unless $status->{$unit} && $status->{$unit} eq '1';
        next unless $serial->{$unit};

        $chassis->{SERIAL} = getCanonicalString($serial->{$unit});

        # From GLPI 10.0.19, we can set discovered stack_number to help GLPI to
        # know which ports are associated with this stack unit
        my $glpi_version = $device->{glpi} ? glpiVersion($device->{glpi}) : 0;
        if (!$glpi_version || $glpi_version >= glpiVersion('10.0.19')) {
            $chassis->{STACK_NUMBER} = $unit;
        }
    }
}

1;

__END__

=head1 NAME

GLPI::Agent::SNMP::MibSupport::Netgear - Inventory module for Netgear devices

=head1 DESCRIPTION

This module enhances Netgear devices support.

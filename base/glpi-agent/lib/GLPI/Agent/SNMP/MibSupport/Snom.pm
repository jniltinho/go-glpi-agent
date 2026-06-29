package GLPI::Agent::SNMP::MibSupport::Snom;

use strict;
use warnings;

use parent 'GLPI::Agent::SNMP::MibSupportTemplate';

use GLPI::Agent::Tools;
use GLPI::Agent::Tools::SNMP;

# See https://service.snom.com/display/wiki/How+to+setup+SNMP
use constant snom       => '.1.3.6.1.2.1.7526';

use constant firmware   => snom . '.2.4';

our $mibSupport = [
    {
        name        => "snom",
        privateoid  => firmware
    }
];

sub new {
    my ($class, %params) = @_;

    my $self = $class->SUPER::new(%params)
        or return;

    bless $self, $class;

    # Disable walk support in device as Snom phones doesn't support GET-NEXT-REQUEST
    $self->device->disableWalk();

    return $self;
}

sub getType {
    return 'NETWORKING';
}

sub getManufacturer {
    return 'Snom';
}

sub getModel {
    my ($self) = @_;

    my $firmware = getCanonicalString($self->get(firmware))
        or return;

    my ($model) = split(/\s+/, $firmware);

    return $model;
}

sub getFirmware {
    my ($self) = @_;

    my $firmware = getCanonicalString($self->get(firmware))
        or return;

    my (undef, $version) = split(/\s+/, $firmware);

    return $version;
}

sub run {
    my ($self) = @_;

    my $device = $self->device
        or return;

    my $firmware = getCanonicalString($self->get(firmware))
        or return;

    my (undef, undef, $uboot) = split(/\s+/, $firmware);
    return if empty($uboot);

    $device->addFirmware({
        NAME            => "Snom Uboot version",
        DESCRIPTION     => "Snom Uboot firmware",
        TYPE            => "system",
        VERSION         => $uboot,
        MANUFACTURER    => "Snom"
    });
}

1;

__END__

=head1 NAME

GLPI::Agent::SNMP::MibSupport::Snom - Inventory module for Snom phones

=head1 DESCRIPTION

This module enhances Snom phones support.

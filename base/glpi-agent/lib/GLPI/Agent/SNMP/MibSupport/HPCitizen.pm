package GLPI::Agent::SNMP::MibSupport::HPCitizen;

use strict;
use warnings;

use parent 'GLPI::Agent::SNMP::MibSupportTemplate';

use GLPI::Agent::Tools;
use GLPI::Agent::Tools::SNMP;

use constant    priority => 8;

use constant    hpCitizen       => '.1.3.6.1.4.1.11.10' ;

# See SEMI-MIB
use constant    hpHttpMgMod     => '.1.3.6.1.4.1.11.2.36.1';

use constant    hpHttpMgNetCitizen  => hpHttpMgMod .'.1.2';

use constant    hpHttpMgManufacturer    => hpHttpMgNetCitizen . '.4.0' ;
use constant    hpHttpMgProduct         => hpHttpMgNetCitizen . '.5.0' ;
use constant    hpHttpMgVersion         => hpHttpMgNetCitizen . '.6.0' ;
use constant    hpHttpMgHWVersion       => hpHttpMgNetCitizen . '.7.0' ;
use constant    hpHttpMgROMVersion      => hpHttpMgNetCitizen . '.8.0' ;
use constant    hpHttpMgSerialNumber    => hpHttpMgNetCitizen . '.9.0' ;

our $mibSupport = [
    {
        name        => "hp-citizen",
        sysobjectid => getRegexpOidMatch(hpCitizen)
    }
];

sub getType {
    return 'STORAGE';
}

sub getManufacturer {
    my ($self) = @_;

    my $manufacturer = getCanonicalString($self->get(hpHttpMgManufacturer));

    return $manufacturer && $manufacturer ne "HP" ? $manufacturer : "Hewlett-Packard";
}

sub getFirmware {
    my ($self) = @_;

    return getCanonicalString($self->get(hpHttpMgVersion));
}

sub getSerial {
    my ($self) = @_;

    return getCanonicalString($self->get(hpHttpMgSerialNumber));
}

sub getModel {
    my ($self) = @_;

    return getCanonicalString($self->get(hpHttpMgProduct));
}

sub run {
    my ($self) = @_;

    my $device = $self->device
        or return;

    my $manufacturer = $self->getManufacturer()
        or return;

    my $model = $self->getModel()
        or return;

    my $hw_version = getCanonicalString($self->get(hpHttpMgHWVersion));
    unless (empty($hw_version)) {
        $device->addFirmware({
            NAME            => "$model HW",
            DESCRIPTION     => "$model HW version",
            TYPE            => "hardware",
            VERSION         => $hw_version,
            MANUFACTURER    => $manufacturer
        });
    }

    my $rom_version = getCanonicalString($self->get(hpHttpMgROMVersion));
    unless (empty($rom_version) || $rom_version =~ /^null$/i ) {
        $device->addFirmware({
            NAME            => "$model Rom",
            DESCRIPTION     => "$model Rom version",
            TYPE            => "hardware",
            VERSION         => $rom_version,
            MANUFACTURER    => $manufacturer
        });
    }
}

1;

__END__

=head1 NAME

GLPI::Agent::SNMP::MibSupport::HPCitizen - Inventory module for HP Storage

=head1 DESCRIPTION

The module enhances HP storage devices support.

#!/usr/bin/perl

use strict;
use warnings;
use lib 't/lib';

use Test::Deep;
use Test::Exception;
use Test::More;
use Test::NoWarnings;

use GLPI::Test::Inventory;
use GLPI::Agent::Task::Inventory::Generic::Ipmi::Lan;

my %tests = (
    'sample1' => {
        DESCRIPTION => 'bmc',
        TYPE        => 'ethernet',
        MANAGEMENT  => 1,
        STATUS      => 'Down',
        IPMASK      => '255.255.255.0',
        MACADDR     => '00:15:17:8f:48:32',
    },
    'RH1288 V3' => {
        DESCRIPTION => 'bmc',
        TYPE        => 'ethernet',
        MANAGEMENT  => 1,
        STATUS      => 'Up',
        IPADDRESS   => '12.34.123.111',
        IPMASK      => '255.255.255.0',
        IPSUBNET    => '12.34.123.0',
        IPGATEWAY   => '12.34.123.254',
        MACADDR     => 'd0:ef:c1:00:de:ad',
    },
);

plan tests => 2 * (scalar keys %tests) + 1;

foreach my $test (keys %tests) {
    my $file = "resources/generic/ipmitool_lan_print/$test";
    my $inventory = GLPI::Test::Inventory->new();

    my $interface = GLPI::Agent::Task::Inventory::Generic::Ipmi::Lan::_getIpmitoolInterface(
        file    => $file,
        logger  => $inventory->{logger}
    );
    cmp_deeply($interface, $tests{$test}, "$test: parsing");

    lives_ok {
        $inventory->addEntry(section => 'NETWORKS', entry => $interface);
    } "$test: registering";
}

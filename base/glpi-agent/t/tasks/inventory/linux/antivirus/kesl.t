#!/usr/bin/perl

use strict;
use warnings;
use lib 't/lib';

use Test::Deep;
use Test::Exception;
use Test::More;
use Test::NoWarnings;

use GLPI::Test::Inventory;
use GLPI::Agent::Task::Inventory::Linux::AntiVirus::KESL;

my %av_tests = (
    'kesl-12.2.0.2412' => {
        COMPANY         => "Kaspersky Lab",
        NAME            => "Kaspersky Endpoint Security for Linux",
        ENABLED         => 1,
        VERSION         => "12.2.0.2412",
        BASE_VERSION    => "2025-06-16",
        EXPIRATION      => "2025-07-18",
        UPTODATE        => 0,
    },
);

plan tests =>
    (2 * scalar keys %av_tests) +
    1;

foreach my $test (keys %av_tests) {
    my $inventory = GLPI::Test::Inventory->new();
    my $base_file = "resources/linux/antivirus/$test";
    my $antivirus = GLPI::Agent::Task::Inventory::Linux::AntiVirus::KESL::_getKESLInfo(
        ksel_appinfo    => $base_file,
        ksel_active     => $base_file."-active",
        logger          => $inventory->{logger}
    );
    cmp_deeply($antivirus, $av_tests{$test}, "$test: parsing");
    lives_ok {
        $inventory->addEntry(section => 'ANTIVIRUS', entry => $antivirus);
    } "$test: registering";
}

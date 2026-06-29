#!/usr/bin/perl

use strict;
use warnings;
use lib 't/lib';

use Test::Deep;
use Test::Exception;
use Test::More;
use Test::NoWarnings;

use GLPI::Test::Inventory;
use GLPI::Agent::Task::Inventory::Linux::AntiVirus::DrWeb;

my %av_tests = (
    'drweb-11.1.16.2406170954' => {
        COMPANY         => "Doctor Web",
        NAME            => "Dr.Web",
        ENABLED         => 1,
        VERSION         => "11.1.16.2406170954",
        BASE_VERSION    => "2025-Jun-16",
        EXPIRATION      => "2025-07-13",
        UPTODATE        => 0,
    },
    'drweb-11.1.16.2406170954-2' => {
        COMPANY         => "Doctor Web",
        NAME            => "Dr.Web",
        ENABLED         => 1,
        VERSION         => "11.1.16.2406170954",
        BASE_VERSION    => "2025-Jun-16",
        UPTODATE        => 0,
    },
);

plan tests =>
    (2 * scalar keys %av_tests) +
    1;

foreach my $test (keys %av_tests) {
    my $inventory = GLPI::Test::Inventory->new();
    my $base_file = "resources/linux/antivirus/$test";
    my $antivirus = GLPI::Agent::Task::Inventory::Linux::AntiVirus::DrWeb::_getDrWebInfo(
        drweb_version   => "$base_file-version",
        drweb_active    => "$base_file-active",
        drweb_baseinfo  => "$base_file-baseinfo",
        drweb_license   => "$base_file-license",
        logger          => $inventory->{logger}
    );
    cmp_deeply($antivirus, $av_tests{$test}, "$test: parsing");
    lives_ok {
        $inventory->addEntry(section => 'ANTIVIRUS', entry => $antivirus);
    } "$test: registering";
}

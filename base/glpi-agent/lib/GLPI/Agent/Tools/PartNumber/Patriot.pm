package GLPI::Agent::Tools::PartNumber::Patriot;

use strict;
use warnings;

use parent 'GLPI::Agent::Tools::PartNumber';

use constant match_re   => undef;

use constant category       => "memory";
use constant manufacturer   => "Patriot Memory";
use constant mm_id          => "Bank 6, Hex 0x02";

1;

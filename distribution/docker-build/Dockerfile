FROM ubuntu:focal AS builder

ENV DEBIAN_FRONTEND noninteractive 
ENV MONO_VERSION 5.18

RUN apt-get update && \
    apt-get -y -o Dpkg::Options::="--force-confold" install --no-install-recommends \
        apt-transport-https \
        wget dirmngr gpg gpg-agent \
        # add-apt-repository for PPAs
        software-properties-common && \
    rm -rf /var/lib/apt/lists/*
	
RUN apt-key adv --keyserver hkp://keyserver.ubuntu.com:80 --recv-keys 3FA7E0328081BFF6A14DA29AA6A19B38D3D831EF && \
    echo "deb http://download.mono-project.com/repo/debian stable-focal main" > /etc/apt/sources.list.d/mono-official-stable.list && \
    apt-get update && apt-get install -y \ 
        devscripts build-essential tofrodos \
        dh-make dh-systemd \
        cli-common-dev \
        mono-complete \
        sqlite3 libcurl4 mediainfo
RUN apt-get upgrade -y

RUN apt-cache policy mono-complete
RUN apt-cache policy cli-common-dev

# NOTE: This is the legacy Debian-PACKAGE BUILDER (note `mono-complete`, `dh-make`,
# `dh-systemd`, `devscripts`, `cli-common-dev` above; CMD `/debian-start.sh` runs
# `debian.sh` to package pre-built bits). It does NOT bake a browser. The Phase-17
# Chromium/BrowserFetcher pre-download block that used to live here was removed in
# #313 (Phase 39 follow-up): Phase 39 retired the embedded-browser Comix signer, so
# there is no browser to bake, and the `tools/ChromiumPrefetch/` helper it invoked
# no longer exists at HEAD (removed in Phase 33.3). The runtime image users PULL is
# authored at distribution/docker/Dockerfile (browser-free since #313).

COPY debian-start.sh /debian-start.sh
RUN  fromdos /debian-start.sh

WORKDIR /data
VOLUME [ "/data/sonarr_bin", "/data/build", "/data/output" ]
CMD /debian-start.sh

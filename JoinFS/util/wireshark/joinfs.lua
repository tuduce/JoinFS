-- JoinFS / JFP2 Wireshark dissector
--
-- Decodes both wire formats JoinFS speaks on one UDP socket:
--   * Legacy protocol: every datagram starts with the 16-bit constant 0x520B
--     (LocalNode.VERSION), written little-endian, so byte 0 on the wire is
--     0x0B. See docs/network/network-protocol.md.
--   * JFP2: every datagram starts with the magic byte 0xFA
--     (JoinFS.Jfp2.Envelope.Magic). See docs/network/jfp2/protocol.md.
--
-- Install: copy this file into your Wireshark "Personal Lua Plugins" folder
-- (Help > About Wireshark > Folders > Personal Lua Plugins), or run Wireshark/
-- tshark with `-X lua_script:joinfs.lua`. Reload Lua plugins with
-- Ctrl+Shift+L (no restart needed) after editing.
--
-- Registers on UDP port 6112 (JoinFS's default, Network.DEFAULT_PORT) and
-- also as a heuristic UDP dissector so it still fires on non-default ports
-- (P2P peers, or a hub configured on a different port).
--
-- CAVEAT: JFP2's per-(peer, message class) schema version is negotiated at
-- Hello/HelloAck time (docs/network/jfp2/protocol.md §5) and is NOT carried on
-- every application datagram - a dissector reading packets in isolation has
-- no reliable way to know which schema version a given peer pair agreed on.
-- As of this writing every JFP2 message class in the codebase
-- (JoinFS/Jfp2/Codecs/*.cs) has exactly one shipped codec, "V1", so this
-- dissector simply assumes schema version 1 everywhere. If a V2 codec (e.g.
-- the quantized PositionV2 the design doc describes) ever ships, the
-- relevant decode_* function below will need a version switch - it will
-- otherwise silently misparse that message class's payload.

local joinfs_proto = Proto("joinfs", "JoinFS / JFP2")

----------------------------------------------------------------------
-- Small portable helpers (avoid depending on a specific Lua bit-op lib,
-- since Wireshark's bundled Lua version varies by build)
----------------------------------------------------------------------

local function band(a, b)
    local result, bitval = 0, 1
    while a > 0 and b > 0 do
        if (a % 2 == 1) and (b % 2 == 1) then
            result = result + bitval
        end
        bitval = bitval * 2
        a = math.floor(a / 2)
        b = math.floor(b / 2)
    end
    return result
end

local function testbit(value, bitindex)
    return band(value, 2 ^ bitindex) ~= 0
end

-- .NET BinaryWriter.Write(string): a 7-bit-encoded ("LEB128-ish") length
-- prefix, MSB of each byte = continuation flag, followed by that many UTF8
-- bytes. Used by every LEGACY string field. (JFP2 strings use a plain
-- 2-byte little-endian length prefix instead - see WireText.cs.)
-- Defensive: any single guess about a still-undocumented/EOF-sensed legacy
-- field order can desync `offset` from the real layout. Rather than let a
-- bogus huge length prefix throw a hard "Range is out of bounds" Lua error
-- (which aborts dissection of the whole packet), clamp to what's actually
-- left and flag it, so the rest of the tree still renders.
local function read_7bit_string(buffer, offset)
    local len = buffer:len()
    local count, shift, pos = 0, 0, offset
    while true do
        if pos >= len then return "<truncated>", pos end
        local b = buffer(pos, 1):uint()
        pos = pos + 1
        count = count + (b % 128) * (2 ^ shift)
        if b < 128 then break end
        shift = shift + 7
    end
    if count == 0 then
        return "", pos
    end
    if pos + count > len then
        return "<truncated, wanted " .. count .. " bytes, only " .. (len - pos) .. " left>", len
    end
    local str = buffer(pos, count):string()
    return str, pos + count
end

-- JFP2 WireText: u16 little-endian length prefix + UTF8 bytes (no 7-bit
-- encoding at all).
local function read_jfp2_string(buffer, offset)
    local totalLen = buffer:len()
    if offset + 2 > totalLen then return "<truncated>", totalLen end
    local strLen = buffer(offset, 2):le_uint()
    offset = offset + 2
    if strLen == 0 then
        return "", offset
    end
    if offset + strLen > totalLen then
        return "<truncated, wanted " .. strLen .. " bytes, only " .. (totalLen - offset) .. " left>", totalLen
    end
    local str = buffer(offset, strLen):string()
    return str, offset + strLen
end

-- A legacy Nuid's `ip` field is packed as (b0<<24)|(b1<<16)|(b2<<8)|b3 (b0 =
-- first dotted-decimal octet) and then written with BinaryWriter.Write(uint),
-- which is little-endian - so the 4 bytes actually on the wire are
-- [b3, b2, b1, b0], the REVERSE of normal dotted-decimal/network byte order.
-- See LocalNode.Nuid's constructor/Write in Node.cs.
local function legacy_nuid_ip_string(buffer, offset)
    local b3 = buffer(offset, 1):uint()
    local b2 = buffer(offset + 1, 1):uint()
    local b1 = buffer(offset + 2, 1):uint()
    local b0 = buffer(offset + 3, 1):uint()
    return string.format("%d.%d.%d.%d", b0, b1, b2, b3)
end

local function add_legacy_nuid(tree, buffer, offset, label)
    local sub = tree:add(buffer(offset, 7), label)
    sub:add(buffer(offset, 4), "IP: " .. legacy_nuid_ip_string(buffer, offset))
    sub:add(buffer(offset + 4, 2), "Port: " .. buffer(offset + 4, 2):le_uint())
    sub:add(buffer(offset + 6, 1), "Local: " .. buffer(offset + 6, 1):uint())
    return offset + 7
end

----------------------------------------------------------------------
-- Name tables (declaration order = wire value for both legacy enums, and
-- the explicit constants for JFP2 - see Node.cs/Network.cs/Jfp2/Envelope.cs)
----------------------------------------------------------------------

-- LocalNode.MESSAGE_ID (Node.cs) - internal/session-management, legacy
local LEGACY_INTERNAL_MSG = {
    [0] = "Join", [1] = "JoinReply", [2] = "AddNode", [3] = "Leave",
    [4] = "Pulse", [5] = "PulseResponse", [6] = "GuaranteedDone",
    [7] = "AddNodes", [8] = "Pathfinder", [9] = "PathfinderResponse",
    [10] = "JoinFail", [11] = "Login", [12] = "LoginFail",
}

-- Network.MESSAGE_ID (Network.cs) - application, legacy
local LEGACY_APP_MSG = {
    [0] = "ObjectPosition", [1] = "AircraftPosition", [2] = "PlaneState",
    [3] = "HelicopterState", [4] = "AircraftState", [5] = "PistonEngineState",
    [6] = "TurbineEngineState", [7] = "AircraftFuel", [8] = "AircraftPayload",
    [9] = "ObjectSmoke", [10] = "WeatherRequest", [11] = "WeatherReply",
    [12] = "WeatherUpdate", [13] = "SharedData", [14] = "StatusRequest",
    [15] = "Status", [16] = "HubList", [17] = "RemoveObject",
    [18] = "UserListRequest", [19] = "UserList", [20] = "UsageLog",
    [21] = "SimEvent", [22] = "KeyLog", [23] = "Shutdown",
    [24] = "SessionCommsRequest", [25] = "GlobalCommsRequest",
    [26] = "CommsListenRequest", [27] = "AllNotesRequest", [28] = "Notes",
    [29] = "UserNuidRequest", [30] = "UserNuid", [31] = "Online",
    [32] = "FlightPlanRequest", [33] = "FlightPlan", [34] = "UserList2",
    [35] = "UserPositionsRequest", [36] = "UserPositions",
    [37] = "IntegerVariables", [38] = "FloatVariables",
    [39] = "String8Variables", [40] = "ShowOnRadar",
}

-- JoinFS.Jfp2.MessageClasses - Internal partition
local JFP2_INTERNAL_CLASS = {
    [0] = "Hello", [1] = "HelloAck", [2] = "Join", [3] = "JoinResult", [4] = "MemberList",
    [5] = "MemberAnnounce", [6] = "MemberLeave", [7] = "RouteQuery", [8] = "RouteAnswer",
    [9] = "GuaranteedDone",
}

-- JoinResult.Result (docs/network/jfp2/protocol.md §5.10); any other value is a refusal
local JFP2_JOIN_RESULT = {
    [0] = "Accepted", [1] = "PasswordRequired", [2] = "LoginRequired",
}

-- JoinFS.Jfp2.MessageClasses - Application partition
local JFP2_APP_CLASS = {
    [0] = "Position", [1] = "Identity", [2] = "VariableSync", [3] = "Event",
    [4] = "FlightPlan", [5] = "Notes", [6] = "Weather", [7] = "Status",
    [8] = "StatusRequest", [9] = "WeatherReply",
    -- the directory (edition 2, §5.16)
    [10] = "HubList", [11] = "UserListRequest", [12] = "HubUser", [13] = "UserPositionsRequest",
    [14] = "UserPositions", [15] = "Online", [16] = "UserLookup", [17] = "UserLookupAnswer",
}

-- Capability bits (docs/network/jfp2/protocol.md §5.6): none assigned yet
local JFP2_CAPABILITY_BITS = {
}

-- HelloAck Result values (docs/network/jfp2/protocol.md §5.2); any other
-- value is unassigned, and every value but 0 means "no session now"
local JFP2_RESULT = {
    [0] = "Accepted", [1] = "NoCompatibleProtoMajor", [2] = "NotAdmitted (a stranger's challenge: a cookie follows, protocol.md §5.9)",
}

-- JoinFS.Jfp2.HandshakeMessage extension tags (docs/network/jfp2/protocol.md §5.4)
local JFP2_TLV_TAG = {
    [1] = "Names", [2] = "Build", [3] = "ObservedEndPoint", [4] = "Admission", [5] = "Candidates",
}

-- JoinFS.Net.Jfp2.WireEndPoint (docs/network/jfp2/protocol.md §4.4): family u8 (4 IPv4, 6 IPv6),
-- the address in network byte order, the port u16 little-endian; 7 or 19 bytes. Returns the text,
-- or nil for an unassigned family or a value shorter than its family needs (a receiver ignores
-- both). Bytes after the endpoint are not read (a later build may extend the value).
local function jfp2_wire_endpoint_string(buffer, offset, len)
    if len < 1 then return nil end
    local family = buffer(offset, 1):uint()
    if family == 4 and len >= 7 then
        local octets = {}
        for i = 0, 3 do octets[#octets + 1] = tostring(buffer(offset + 1 + i, 1):uint()) end
        return table.concat(octets, ".") .. ":" .. buffer(offset + 5, 2):le_uint()
    elseif family == 6 and len >= 19 then
        local groups = {}
        for i = 0, 7 do groups[#groups + 1] = string.format("%x", buffer(offset + 1 + i * 2, 2):uint()) end
        return "[" .. table.concat(groups, ":") .. "]:" .. buffer(offset + 17, 2):le_uint()
    end
    return nil
end

-- JoinFS.Net.Jfp2.NodeName kinds (docs/network/jfp2/protocol.md §4.3)
local JFP2_NAME_KIND = {
    [0] = "legacy id", [1] = "random key", [2] = "key-pair id", [255] = "group name",
}

-- A JFP2 node name: 8 bytes, a kind byte then 7 bytes by kind. Kind 0 holds a
-- legacy Nuid in the legacy header's layout; other kinds are shown as hex.
local function add_jfp2_name(tree, buffer, offset, label)
    local kind = buffer(offset, 1):uint()
    local kindName = JFP2_NAME_KIND[kind] or "unassigned"
    local sub
    if kind == 0 then
        sub = tree:add(buffer(offset, 8), label .. ": " .. legacy_nuid_ip_string(buffer, offset + 1) ..
            ":" .. buffer(offset + 5, 2):le_uint() .. "/" .. buffer(offset + 7, 1):uint() .. " (kind 0, " .. kindName .. ")")
        sub:add(buffer(offset, 1), "Kind: 0 (" .. kindName .. ")")
        add_legacy_nuid(sub, buffer, offset + 1, "Legacy id")
    else
        sub = tree:add(buffer(offset, 8), label .. ": kind " .. kind .. " (" .. kindName .. ") " ..
            buffer(offset + 1, 7):bytes():tohex())
        sub:add(buffer(offset, 1), "Kind: " .. kind .. " (" .. kindName .. ")")
        sub:add(buffer(offset + 1, 7), "Value: " .. buffer(offset + 1, 7):bytes():tohex())
    end
    return offset + 8
end

----------------------------------------------------------------------
-- Legacy payload decoders (docs/network/network-protocol.md §9)
----------------------------------------------------------------------

local function decode_legacy_internal(tree, buffer, offset, msgId)
    local name = LEGACY_INTERNAL_MSG[msgId]
    local len = buffer:len()
    if name == "Join" then
        tree:add(buffer(offset, 4), "PasswordHash: " .. buffer(offset, 4):le_uint())
    elseif name == "JoinReply" then
        -- Each entry is a full 7-byte Nuid (itself carrying a port) PLUS a
        -- separate routing Port field written right after it - see the
        -- foreach loop in Node.cs's Join handler (`otherNode.Key.Write(...)`
        -- followed by `sendWriter.Write((ushort)otherNode.Value.endPoint.Port)`).
        tree:add(buffer(offset, 4), "Suid: " .. buffer(offset, 4):le_uint()); offset = offset + 4
        local count = buffer(offset, 2):le_uint()
        tree:add(buffer(offset, 2), "Count: " .. count); offset = offset + 2
        for i = 1, count do
            offset = add_legacy_nuid(tree, buffer, offset, "Node " .. i)
            tree:add(buffer(offset, 2), "Node " .. i .. " routing Port: " .. buffer(offset, 2):le_uint())
            offset = offset + 2
        end
    elseif name == "JoinFail" then
        local result = buffer(offset, 1):uint()
        local names = { [0] = "Accepted", [1] = "PasswordRequired", [2] = "LoginRequired" }
        tree:add(buffer(offset, 1), "Result: " .. (names[result] or result))
    elseif name == "Login" then
        local email; email, offset = read_7bit_string(buffer, offset)
        tree:add(buffer(),"Email: " .. email)
        tree:add(buffer(offset, 4), "PasswordHash: " .. buffer(offset, 4):le_uint()); offset = offset + 4
        tree:add(buffer(offset, 1), "Verify: " .. buffer(offset, 1):uint())
    elseif name == "LoginFail" then
        local result = buffer(offset, 1):uint()
        local names = { [0] = "Accepted", [1] = "InvalidAddress", [2] = "VerifyPassword", [3] = "InvalidPassword" }
        tree:add(buffer(offset, 1), "Result: " .. (names[result] or result))
    elseif name == "Leave" then
        tree:add(buffer(offset, 4), "Suid: " .. buffer(offset, 4):le_uint())
    elseif name == "AddNode" then
        tree:add(buffer(offset, 4), "Suid: " .. buffer(offset, 4):le_uint()); offset = offset + 4
        offset = add_legacy_nuid(tree, buffer, offset, "Nuid")
        tree:add(buffer(offset, 2), "Port: " .. buffer(offset, 2):le_uint())
    elseif name == "Pulse" then
        tree:add(buffer(offset, 4), "Suid: " .. buffer(offset, 4):le_uint()); offset = offset + 4
        tree:add(buffer(offset, 8), "Time: " .. buffer(offset, 8):le_uint64():tonumber()); offset = offset + 8
        local flags = buffer(offset, 1):uint()
        tree:add(buffer(offset, 1), "Flags: 0x" .. string.format("%02x", flags) ..
            (testbit(flags, 0) and " (LowBandwidth)" or ""))
    elseif name == "PulseResponse" then
        tree:add(buffer(offset, 8), "Time (echo): " .. buffer(offset, 8):le_uint64():tonumber())
    elseif name == "Pathfinder" or name == "PathfinderResponse" then
        tree:add(buffer(offset, 4), "Suid: " .. buffer(offset, 4):le_uint()); offset = offset + 4
        local count = buffer(offset, 2):le_uint()
        tree:add(buffer(offset, 2), "Count: " .. count); offset = offset + 2
        for i = 1, count do
            offset = add_legacy_nuid(tree, buffer, offset, "Nuid " .. i)
        end
    elseif name == "GuaranteedDone" then
        tree:add(buffer(offset, 2), "GuaranteedId: " .. buffer(offset, 2):le_uint()); offset = offset + 2
        tree:add(buffer(offset, 1), "GuaranteedIndex: " .. buffer(offset, 1):uint())
    elseif offset < len then
        tree:add(buffer(offset, len - offset), "Payload (undecoded, " .. (len - offset) .. " bytes)")
    end
end

-- ObjectPosition / AircraftPosition share a lot of shape; `hasIdentityHead`
-- selects the AircraftPosition variant (NetId/User/IsPlane/Callsign up
-- front) vs ObjectPosition's plainer head (NetId/Model/TypeRole/Flags).
local function decode_legacy_position(tree, buffer, offset, isAircraft)
    local len = buffer:len()
    tree:add(buffer(offset, 4), "NetId: " .. buffer(offset, 4):le_uint()); offset = offset + 4
    if isAircraft then
        tree:add(buffer(offset, 1), "User: " .. buffer(offset, 1):uint()); offset = offset + 1
        tree:add(buffer(offset, 1), "IsPlane: " .. buffer(offset, 1):uint()); offset = offset + 1
        local s; s, offset = read_7bit_string(buffer, offset); tree:add(buffer(),"Callsign: " .. s)
        s, offset = read_7bit_string(buffer, offset); tree:add(buffer(),"Model: " .. s)
    else
        local s; s, offset = read_7bit_string(buffer, offset); tree:add(buffer(),"Model: " .. s)
    end
    tree:add(buffer(offset, 1), "TypeRole: " .. buffer(offset, 1):uint()); offset = offset + 1
    local flags = buffer(offset, 1):uint()
    tree:add(buffer(offset, 1), "Flags: 0x" .. string.format("%02x", flags) ..
        (testbit(flags, 0) and " (Paused)" or "")); offset = offset + 1
    tree:add(buffer(offset, 8), "NetTime/SimTime: " .. buffer(offset, 8):le_float()); offset = offset + 8
    tree:add(buffer(offset, 8), "Latitude: " .. buffer(offset, 8):le_float()); offset = offset + 8
    tree:add(buffer(offset, 8), "Longitude: " .. buffer(offset, 8):le_float()); offset = offset + 8
    tree:add(buffer(offset, 8), "Altitude: " .. buffer(offset, 8):le_float()); offset = offset + 8
    tree:add(buffer(offset, 4), "Pitch: " .. buffer(offset, 4):le_float()); offset = offset + 4
    tree:add(buffer(offset, 4), "Bank: " .. buffer(offset, 4):le_float()); offset = offset + 4
    tree:add(buffer(offset, 4), "Heading: " .. buffer(offset, 4):le_float()); offset = offset + 4
    local vecs = { "VelocityX", "VelocityY", "VelocityZ", "AngularVelocityX", "AngularVelocityY",
        "AngularVelocityZ", "AccelerationX", "AccelerationY", "AccelerationZ" }
    for _, label in ipairs(vecs) do
        tree:add(buffer(offset, 4), label .. ": " .. buffer(offset, 4):le_float()); offset = offset + 4
    end
    if isAircraft then
        local axes = { "Rudder", "Elevator", "Aileron", "BrakeLeft", "BrakeRight" }
        for _, label in ipairs(axes) do
            local raw = buffer(offset, 2):le_int()
            tree:add(buffer(offset, 2), label .. ": " .. (raw / 16384.0) .. " (raw " .. raw .. ")")
            offset = offset + 2
        end
        tree:add(buffer(offset, 4), "Elevation: " .. buffer(offset, 4):le_float()); offset = offset + 4
    else
        tree:add(buffer(offset, 4), "Height: " .. buffer(offset, 4):le_float()); offset = offset + 4
    end
    local groundFlags = buffer(offset, 1):uint()
    tree:add(buffer(offset, 1), "GroundFlags: 0x" .. string.format("%02x", groundFlags) ..
        (testbit(groundFlags, 0) and " (OnGround)" or "") ..
        (testbit(groundFlags, 1) and " (ElevationCorrection)" or "")); offset = offset + 1
    -- Trailing fields added over time; legacy readers EOF-sense these (see
    -- network-protocol.md §3.5). Order and presence per-field verified against
    -- Network.cs's WriteObjectPositionVelocityMessage/WriteAircraftPositionMessage
    -- (NOT the network-protocol.md table, whose row order doesn't match the
    -- actual wire order for StaticCgToGround - it's written LAST, after
    -- ClassCodeConfirmed, not right after GroundFlags).
    local trailing = { "Livery", "IcaoType", "IcaoAirline" }
    if isAircraft then
        table.insert(trailing, "Registration")
        table.insert(trailing, "FlightNumber")
    end
    table.insert(trailing, "ClassCode")
    table.insert(trailing, "Wtc")
    for _, label in ipairs(trailing) do
        if offset >= len then return end
        local s; s, offset = read_7bit_string(buffer, offset)
        tree:add(buffer(),label .. ": " .. s)
    end
    if offset >= len then return end
    tree:add(buffer(offset, 1), "ClassCodeConfirmed: " .. buffer(offset, 1):uint()); offset = offset + 1
    if isAircraft and offset + 4 <= len then
        tree:add(buffer(offset, 4), "StaticCgToGround: " .. buffer(offset, 4):le_float()); offset = offset + 4
    end
end

local function decode_legacy_shared_data(tree, buffer, offset)
    local len = buffer:len()
    local shareFlags = buffer(offset, 1):uint()
    tree:add(buffer(offset, 1), "ShareFlags: 0x" .. string.format("%02x", shareFlags)); offset = offset + 1
    local s; s, offset = read_7bit_string(buffer, offset); tree:add(buffer(),"Nickname: " .. s)
    tree:add(buffer(offset, 16), "Guid: " .. buffer(offset, 16):bytes():tohex()); offset = offset + 16
    local flags = buffer(offset, 1):uint()
    tree:add(buffer(offset, 1), "Flags: 0x" .. string.format("%02x", flags) ..
        (testbit(flags, 0) and " (Hub)" or "") ..
        (testbit(flags, 1) and " (Atc)" or "") ..
        (testbit(flags, 2) and " (SimConnected)" or "")); offset = offset + 1
    s, offset = read_7bit_string(buffer, offset); tree:add(buffer(),"AtcAirport: " .. s)
    if offset >= len then return end
    tree:add(buffer(offset, 1), "AtcLevel: " .. buffer(offset, 1):uint()); offset = offset + 1
    tree:add(buffer(offset, 2), "AtcFrequency: " .. buffer(offset, 2):le_uint()); offset = offset + 2
    tree:add(buffer(offset, 1), "ActivityCircle: " .. buffer(offset, 1):uint()); offset = offset + 1
    if offset >= len then return end
    s, offset = read_7bit_string(buffer, offset); tree:add(buffer(),"Version: " .. s)
    if offset >= len then return end
    s, offset = read_7bit_string(buffer, offset); tree:add(buffer(),"Simulator: " .. s)
end

local function decode_legacy_variables(tree, buffer, offset, kind)
    local len = buffer:len()
    offset = add_legacy_nuid(tree, buffer, offset, "OwnerNuid")
    tree:add(buffer(offset, 4), "NetId: " .. buffer(offset, 4):le_uint()); offset = offset + 4
    local count = buffer(offset, 2):le_uint()
    tree:add(buffer(offset, 2), "Count: " .. count); offset = offset + 2
    for i = 1, count do
        if offset >= len then break end
        local vid = buffer(offset, 4):le_uint(); offset = offset + 4
        if kind == "int" then
            tree:add(buffer(offset - 4, 8), "VariableId " .. vid .. ": " .. buffer(offset, 4):le_int())
            offset = offset + 4
        elseif kind == "float" then
            tree:add(buffer(offset - 4, 8), "VariableId " .. vid .. ": " .. buffer(offset, 4):le_float())
            offset = offset + 4
        else
            local s; s, offset = read_7bit_string(buffer, offset)
            tree:add(buffer(),"VariableId " .. vid .. ": \"" .. s .. "\"")
        end
    end
end

local function decode_legacy_application(tree, buffer, offset, msgId)
    local name = LEGACY_APP_MSG[msgId]
    local len = buffer:len()
    if name == "ObjectPosition" then
        decode_legacy_position(tree, buffer, offset, false)
    elseif name == "AircraftPosition" then
        decode_legacy_position(tree, buffer, offset, true)
    elseif name == "SharedData" then
        decode_legacy_shared_data(tree, buffer, offset)
    elseif name == "WeatherRequest" then
        tree:add(buffer(offset, 4), "NetId: " .. buffer(offset, 4):le_uint())
    elseif name == "WeatherReply" or name == "WeatherUpdate" then
        local s = select(1, read_7bit_string(buffer, offset))
        tree:add(buffer(offset, len - offset), "Metar: " .. s)
    elseif name == "StatusRequest" then
        -- WriteStatusRequestMessage: two separate bool bytes, NOT a bitfield.
        tree:add(buffer(offset, 1), "HubEnabled: " .. buffer(offset, 1):uint()); offset = offset + 1
        tree:add(buffer(offset, 1), "HubListRequested: " .. buffer(offset, 1):uint()); offset = offset + 1
        tree:add(buffer(offset, 4), "Uuid: 0x" .. string.format("%08x", buffer(offset, 4):le_uint()))
    elseif name == "Status" then
        -- WriteStatusMessage: AtcAirport/AtcLevel only present when AtcCount>0;
        -- the whole hub-details block only present when HubEnabled is true.
        tree:add(buffer(offset, 16), "Guid: " .. buffer(offset, 16):bytes():tohex()); offset = offset + 16
        local s; s, offset = read_7bit_string(buffer, offset); tree:add(buffer(), "AppVersion: " .. s)
        tree:add(buffer(offset, 2), "Users: " .. buffer(offset, 2):le_uint()); offset = offset + 2
        local atcCount = buffer(offset, 2):le_uint()
        tree:add(buffer(offset, 2), "AtcCount: " .. atcCount); offset = offset + 2
        if atcCount > 0 then
            s, offset = read_7bit_string(buffer, offset); tree:add(buffer(), "AtcAirport: " .. s)
            tree:add(buffer(offset, 1), "AtcLevel: " .. buffer(offset, 1):uint()); offset = offset + 1
        end
        tree:add(buffer(offset, 2), "Planes: " .. buffer(offset, 2):le_uint()); offset = offset + 2
        tree:add(buffer(offset, 2), "Helicopters: " .. buffer(offset, 2):le_uint()); offset = offset + 2
        tree:add(buffer(offset, 2), "Boats: " .. buffer(offset, 2):le_uint()); offset = offset + 2
        tree:add(buffer(offset, 2), "Vehicles: " .. buffer(offset, 2):le_uint()); offset = offset + 2
        local hubEnabled = buffer(offset, 1):uint()
        tree:add(buffer(offset, 1), "HubEnabled: " .. hubEnabled); offset = offset + 1
        if hubEnabled ~= 0 then
            s, offset = read_7bit_string(buffer, offset); tree:add(buffer(), "Address: " .. s)
            s, offset = read_7bit_string(buffer, offset); tree:add(buffer(), "Name: " .. s)
            s, offset = read_7bit_string(buffer, offset); tree:add(buffer(), "About: " .. s)
            s, offset = read_7bit_string(buffer, offset); tree:add(buffer(), "Voip: " .. s)
            s, offset = read_7bit_string(buffer, offset); tree:add(buffer(), "NextEvent: " .. s)
            s, offset = read_7bit_string(buffer, offset); tree:add(buffer(), "Airport: " .. s)
            tree:add(buffer(offset, 1), "ActivityCircle: " .. buffer(offset, 1):uint()); offset = offset + 1
            local hubFlags = buffer(offset, 1):uint()
            tree:add(buffer(offset, 1), "Flags: 0x" .. string.format("%02x", hubFlags) ..
                (testbit(hubFlags, 1) and " (GlobalSession)" or "") ..
                (testbit(hubFlags, 2) and " (PasswordRequired)" or ""))
        end
    elseif name == "RemoveObject" then
        tree:add(buffer(offset, 4), "NetId: " .. buffer(offset, 4):le_uint())
    elseif name == "SimEvent" then
        tree:add(buffer(offset, 4), "NetId: " .. buffer(offset, 4):le_uint()); offset = offset + 4
        tree:add(buffer(offset, 4), "EventId: " .. buffer(offset, 4):le_uint()); offset = offset + 4
        tree:add(buffer(offset, 4), "Data: " .. buffer(offset, 4):le_uint())
    elseif name == "ShowOnRadar" then
        offset = add_legacy_nuid(tree, buffer, offset, "OwnerNuid")
        tree:add(buffer(offset, 4), "NetId: " .. buffer(offset, 4):le_uint()); offset = offset + 4
        tree:add(buffer(offset, 1), "Show: " .. buffer(offset, 1):uint())
    elseif name == "IntegerVariables" then
        decode_legacy_variables(tree, buffer, offset, "int")
    elseif name == "FloatVariables" then
        decode_legacy_variables(tree, buffer, offset, "float")
    elseif name == "String8Variables" then
        decode_legacy_variables(tree, buffer, offset, "string")
    elseif name == "FlightPlan" then
        -- WriteFlightPlanMessage: OwnerNuid + NetId + a fixed Version byte precede
        -- the string fields, and the string ORDER is not what network-protocol.md's
        -- table implies (Registration/IcaoAirline/FlightNumber are NOT adjacent to
        -- IcaoType - IcaoAirline/FlightNumber trail at the very end).
        offset = add_legacy_nuid(tree, buffer, offset, "OwnerNuid")
        tree:add(buffer(offset, 4), "NetId: " .. buffer(offset, 4):le_uint()); offset = offset + 4
        tree:add(buffer(offset, 1), "Version: " .. buffer(offset, 1):uint()); offset = offset + 1
        local labels = { "IcaoType", "Departure", "Destination", "Rules", "Route", "Remarks",
            "Alternate", "Speed", "Altitude", "Callsign", "Registration", "IcaoAirline", "FlightNumber" }
        for _, label in ipairs(labels) do
            if offset >= len then break end
            local s; s, offset = read_7bit_string(buffer, offset)
            tree:add(buffer(),label .. ": " .. s)
        end
    elseif offset < len then
        tree:add(buffer(offset, len - offset), (name or ("Unknown (" .. msgId .. ")")) ..
            " payload (undecoded, " .. (len - offset) .. " bytes)")
    end
end

----------------------------------------------------------------------
-- Legacy dissector: 21-byte transport header (network-protocol.md §3.1),
-- then either an internal message (no DataVersion) or an application
-- message (DataVersion + MessageId, §5) per FLAG_INTERNAL.
----------------------------------------------------------------------

local function dissect_legacy(buffer, pinfo, tree)
    pinfo.cols.protocol = "JoinFS"
    local subtree = tree:add(joinfs_proto, buffer(), "JoinFS Legacy Protocol")

    subtree:add(buffer(0, 2), "Version: 0x" .. string.format("%04x", buffer(0, 2):le_uint()))
    local flags = buffer(2, 1):uint()
    local isInternal = testbit(flags, 0)
    local isGuaranteed = testbit(flags, 1)
    local isForward = testbit(flags, 2)
    subtree:add(buffer(2, 1), "Flags: 0x" .. string.format("%02x", flags) ..
        (isInternal and " [Internal]" or " [Application]") ..
        (isGuaranteed and " [Guaranteed]" or "") ..
        (isForward and " [Forwarded]" or ""))
    subtree:add(buffer(3, 2), "GuaranteedId: " .. buffer(3, 2):le_uint())
    subtree:add(buffer(5, 1), "GuaranteedIndex: " .. buffer(5, 1):uint())
    subtree:add(buffer(6, 1), "GuaranteedCount: " .. buffer(6, 1):uint())
    add_legacy_nuid(subtree, buffer, 7, "Sender")
    add_legacy_nuid(subtree, buffer, 14, "Recipient")

    local offset = 21
    if buffer:len() <= offset then
        pinfo.cols.info = "JoinFS Legacy (header only)"
        return
    end

    if isInternal then
        local msgId = buffer(offset, 2):le_uint()
        local name = LEGACY_INTERNAL_MSG[msgId] or ("Unknown(" .. msgId .. ")")
        pinfo.cols.info = "JoinFS Legacy Internal: " .. name .. (isForward and " [fwd]" or "")
        local msgtree = subtree:add(buffer(offset, buffer:len() - offset), "Internal message: " .. name)
        msgtree:add(buffer(offset, 2), "MessageId: " .. msgId .. " (" .. name .. ")")
        local ok, err = pcall(decode_legacy_internal, msgtree, buffer, offset + 2, msgId)
        if not ok then msgtree:add_expert_info(PI_MALFORMED, PI_ERROR, "joinfs.lua decode error: " .. tostring(err)) end
    else
        local dataVersion = buffer(offset, 2):le_uint()
        local msgId = buffer(offset + 2, 2):le_uint()
        local name = LEGACY_APP_MSG[msgId] or ("Unknown(" .. msgId .. ")")
        pinfo.cols.info = "JoinFS Legacy: " .. name .. (isForward and " [fwd]" or "") ..
            " (dataVersion " .. dataVersion .. ")"
        local msgtree = subtree:add(buffer(offset, buffer:len() - offset), "Application message: " .. name)
        msgtree:add(buffer(offset, 2), "DataVersion: " .. dataVersion)
        msgtree:add(buffer(offset + 2, 2), "MessageId: " .. msgId .. " (" .. name .. ")")
        local ok, err = pcall(decode_legacy_application, msgtree, buffer, offset + 4, msgId)
        if not ok then msgtree:add_expert_info(PI_MALFORMED, PI_ERROR, "joinfs.lua decode error: " .. tostring(err)) end
    end
end

----------------------------------------------------------------------
-- JFP2 payload decoders (docs/network/jfp2/protocol.md §9, JoinFS/Jfp2/*)
----------------------------------------------------------------------

local function decode_jfp2_handshake(tree, buffer, offset, isAck)
    tree:add(buffer(offset, 1), "ProtoMajorMin: " .. buffer(offset, 1):uint()); offset = offset + 1
    tree:add(buffer(offset, 1), "ProtoMajorMax: " .. buffer(offset, 1):uint()); offset = offset + 1
    local caps = buffer(offset, 8):le_uint64()
    local capNames = {}
    for bitIdx, capName in pairs(JFP2_CAPABILITY_BITS) do
        if caps:band(UInt64(1):lshift(bitIdx)) ~= UInt64(0) then
            table.insert(capNames, capName)
        end
    end
    tree:add(buffer(offset, 8), "Capabilities: 0x" .. caps:tohex() ..
        (#capNames > 0 and (" [" .. table.concat(capNames, ", ") .. "]") or "")); offset = offset + 8
    tree:add(buffer(offset, 2), "SelfAssignedId: " .. buffer(offset, 2):le_uint()); offset = offset + 2
    local result = buffer(offset, 1):uint()
    if isAck then
        tree:add(buffer(offset, 1), "Result: " .. result .. " (" .. (JFP2_RESULT[result] or "unassigned") ..
            (result == 0 and ")" or ": no session now)"))
    else
        tree:add(buffer(offset, 1), "Result: " .. result .. " (ignored on Hello)")
    end
    offset = offset + 1
    local offerCount = buffer(offset, 2):le_uint()
    tree:add(buffer(offset, 2), "OfferCount: " .. offerCount); offset = offset + 2
    for i = 1, offerCount do
        local partition = buffer(offset, 1):uint()
        local class = buffer(offset + 1, 1):uint()
        local minV = buffer(offset + 2, 1):uint()
        local maxV = buffer(offset + 3, 1):uint()
        if partition == 0 or partition == 1 then
            local partName = partition == 1 and "Internal" or "Application"
            local className = partition == 1 and (JFP2_INTERNAL_CLASS[class] or class) or (JFP2_APP_CLASS[class] or class)
            tree:add(buffer(offset, 4), "Offer " .. i .. ": " .. partName .. "/" .. className ..
                " v[" .. minV .. ".." .. maxV .. "]")
        else
            tree:add(buffer(offset, 4), "Offer " .. i .. ": unknown partition " .. partition ..
                ", class " .. class .. " v[" .. minV .. ".." .. maxV .. "] (skipped)")
        end
        offset = offset + 4
    end
    local len = buffer:len()
    if offset < len then
        local ext = tree:add(buffer(offset, len - offset), "Extensions (TLV)")
        local seen = {}
        while offset + 4 <= len do
            local tag = buffer(offset, 2):le_uint()
            local tlvLen = buffer(offset + 2, 2):le_uint()
            if offset + 4 + tlvLen > len then break end
            local tagName = JFP2_TLV_TAG[tag]
            local repeated = seen[tag]
            seen[tag] = true
            local item = ext:add(buffer(offset, 4 + tlvLen), "Tag 0x" .. string.format("%04x", tag) ..
                (tagName and (" (" .. tagName .. ")") or "") .. ", " .. tlvLen .. " bytes" ..
                (repeated and " (repeated: ignored, the first copy counts)" or ""))
            if repeated then
                -- nothing more: a receiver reads the first copy only
            elseif tagName == "Names" then
                -- whole names; a remainder shorter than a name is skipped
                local count = math.floor(tlvLen / 8)
                for i = 0, count - 1 do
                    add_jfp2_name(item, buffer, offset + 4 + i * 8, "Name " .. (i + 1))
                end
                if tlvLen > count * 8 then
                    item:add(buffer(offset + 4 + count * 8, tlvLen - count * 8), "Remainder: " .. (tlvLen - count * 8) .. " bytes (skipped)")
                end
            elseif tagName == "Build" and tlvLen > 0 then
                item:add(buffer(offset + 4, tlvLen), "Build: " .. buffer(offset + 4, tlvLen):string(ENC_UTF_8))
            elseif tagName == "Admission" then
                -- docs/network/jfp2/protocol.md §5.9: empty in a Hello asks for admission, 16 bytes is a cookie
                if tlvLen == 0 then
                    item:add(buffer(offset + 4, 0), "Admission: empty (a stranger asks to be admitted)")
                elseif tlvLen >= 16 then
                    item:add(buffer(offset + 4, 16), "Admission cookie: " .. tostring(buffer(offset + 4, 16):bytes()) ..
                        (tlvLen > 16 and " (the rest is skipped)" or "") ..
                        (isAck and (result == 2 and " (the challenge)" or "") or " (echoed)"))
                else
                    item:add(buffer(offset + 4, tlvLen), "Admission: " .. tlvLen .. " bytes, too short (ignored)")
                end
            elseif tagName == "Candidates" then
                -- docs/network/jfp2/protocol.md §5.14: entries, each a length byte and a WireEndPoint; at most 8 are read
                local at = offset + 4
                local stop = offset + 4 + tlvLen
                local n = 0
                while at < stop and n < 8 do
                    local l = buffer(at, 1):uint()
                    if at + 1 + l > stop then break end
                    n = n + 1
                    local text = jfp2_wire_endpoint_string(buffer, at + 1, l)
                    item:add(buffer(at, 1 + l), "Candidate " .. n .. ": " .. (text or "unassigned family or too short (skipped)"))
                    at = at + 1 + l
                end
            elseif tagName == "ObservedEndPoint" and tlvLen > 0 then
                -- the UDP source of the Hello this HelloAck answers, as the responder received it
                local text = jfp2_wire_endpoint_string(buffer, offset + 4, tlvLen)
                if text then
                    item:add(buffer(offset + 4, tlvLen), "ObservedEndPoint: " .. text ..
                        (isAck and "" or " (only a HelloAck carries it)"))
                else
                    item:add(buffer(offset + 4, tlvLen), "ObservedEndPoint: family " .. buffer(offset + 4, 1):uint() ..
                        ", unassigned or too short (ignored)")
                end
            end
            offset = offset + 4 + tlvLen
        end
    end
end

local function decode_jfp2_position_v1(tree, buffer, offset)
    tree:add(buffer(offset, 4), "ObjectId: " .. buffer(offset, 4):le_uint()); offset = offset + 4
    tree:add(buffer(offset, 8), "NetTime: " .. buffer(offset, 8):le_float()); offset = offset + 8
    tree:add(buffer(offset, 8), "Latitude: " .. buffer(offset, 8):le_float()); offset = offset + 8
    tree:add(buffer(offset, 8), "Longitude: " .. buffer(offset, 8):le_float()); offset = offset + 8
    tree:add(buffer(offset, 8), "Altitude: " .. buffer(offset, 8):le_float()); offset = offset + 8
    local floats = { "Pitch", "Bank", "Heading", "VelocityX", "VelocityY", "VelocityZ",
        "AngularVelocityX", "AngularVelocityY", "AngularVelocityZ",
        "AccelerationX", "AccelerationY", "AccelerationZ" }
    for _, label in ipairs(floats) do
        tree:add(buffer(offset, 4), label .. ": " .. buffer(offset, 4):le_float()); offset = offset + 4
    end
    local axes = { "Rudder", "Elevator", "Aileron", "BrakeLeft", "BrakeRight" }
    for _, label in ipairs(axes) do
        local raw = buffer(offset, 2):le_int()
        tree:add(buffer(offset, 2), label .. ": " .. (raw / 16384.0) .. " (raw " .. raw .. ")")
        offset = offset + 2
    end
    tree:add(buffer(offset, 4), "Elevation: " .. buffer(offset, 4):le_float()); offset = offset + 4
    tree:add(buffer(offset, 4), "StaticCgToGround: " .. buffer(offset, 4):le_float()); offset = offset + 4
    local stateFlags = buffer(offset, 1):uint()
    tree:add(buffer(offset, 1), "StateFlags: 0x" .. string.format("%02x", stateFlags) ..
        (testbit(stateFlags, 0) and " (OnGround)" or "") ..
        (testbit(stateFlags, 1) and " (ElevationCorrection)" or "") ..
        (testbit(stateFlags, 2) and " (UserControlled)" or "") ..
        (testbit(stateFlags, 3) and " (Paused)" or ""))
end

local function decode_jfp2_identity_v1(tree, buffer, offset)
    tree:add(buffer(offset, 4), "ObjectId: " .. buffer(offset, 4):le_uint()); offset = offset + 4
    local flags = buffer(offset, 1):uint()
    tree:add(buffer(offset, 1), "Flags: 0x" .. string.format("%02x", flags) ..
        (testbit(flags, 0) and " (IsAircraft)" or "") ..
        (testbit(flags, 1) and " (IsPlane)" or "") ..
        (testbit(flags, 2) and " (ClassCodeConfirmed)" or "")); offset = offset + 1
    tree:add(buffer(offset, 1), "TypeRole: " .. buffer(offset, 1):uint()); offset = offset + 1
    local labels = { "Callsign", "Model", "Livery", "IcaoType", "IcaoAirline",
        "Registration", "FlightNumber", "ClassCode", "Wtc" }
    for _, label in ipairs(labels) do
        local s; s, offset = read_jfp2_string(buffer, offset)
        tree:add(buffer(),label .. ": " .. s)
    end
end

local function decode_jfp2_variable_sync_v1(tree, buffer, offset)
    tree:add(buffer(offset, 4), "ObjectId: " .. buffer(offset, 4):le_uint()); offset = offset + 4
    local count = buffer(offset, 1):uint()
    tree:add(buffer(offset, 1), "EntryCount: " .. count); offset = offset + 1
    local kindNames = { [0] = "Int32", [1] = "Float32", [2] = "String8" }
    for i = 1, count do
        local vuid = buffer(offset, 4):le_uint(); offset = offset + 4
        local kind = buffer(offset, 1):uint(); offset = offset + 1
        local kindName = kindNames[kind] or kind
        if kind == 0 then
            tree:add(buffer(offset - 5, 9), "Entry " .. i .. ": vuid=0x" .. string.format("%08x", vuid) ..
                " " .. kindName .. " = " .. buffer(offset, 4):le_int())
            offset = offset + 4
        elseif kind == 1 then
            tree:add(buffer(offset - 5, 9), "Entry " .. i .. ": vuid=0x" .. string.format("%08x", vuid) ..
                " " .. kindName .. " = " .. buffer(offset, 4):le_float())
            offset = offset + 4
        else
            local s; s, offset = read_jfp2_string(buffer, offset)
            tree:add(buffer(),"Entry " .. i .. ": vuid=0x" .. string.format("%08x", vuid) ..
                " " .. kindName .. " = \"" .. s .. "\"")
        end
    end
end

local function decode_jfp2_event_v1(tree, buffer, offset)
    tree:add(buffer(offset, 4), "ObjectId: " .. buffer(offset, 4):le_uint()); offset = offset + 4
    tree:add(buffer(offset, 4), "EventId: " .. buffer(offset, 4):le_uint()); offset = offset + 4
    tree:add(buffer(offset, 4), "Data: " .. buffer(offset, 4):le_uint())
end

local function decode_jfp2_flightplan_v1(tree, buffer, offset)
    tree:add(buffer(offset, 4), "ObjectId: " .. buffer(offset, 4):le_uint()); offset = offset + 4
    local labels = { "IcaoType", "Departure", "Destination", "Rules", "Route", "Remarks",
        "Alternate", "Speed", "Altitude", "Callsign", "Registration", "IcaoAirline", "FlightNumber" }
    for _, label in ipairs(labels) do
        local s; s, offset = read_jfp2_string(buffer, offset)
        tree:add(buffer(),label .. ": " .. s)
    end
end

local function decode_jfp2_notes_v1(tree, buffer, offset)
    tree:add(buffer(offset, 16), "Guid: " .. buffer(offset, 16):bytes():tohex()); offset = offset + 16
    local s
    s, offset = read_jfp2_string(buffer, offset); tree:add(buffer(),"Nickname: " .. s)
    s, offset = read_jfp2_string(buffer, offset); tree:add(buffer(),"Callsign: " .. s)
    tree:add(buffer(offset, 4), "NoteId: " .. buffer(offset, 4):le_uint()); offset = offset + 4
    tree:add(buffer(offset, 4), "Age: " .. buffer(offset, 4):le_float()); offset = offset + 4
    tree:add(buffer(offset, 2), "Channel: " .. buffer(offset, 2):le_uint()); offset = offset + 2
    s, offset = read_jfp2_string(buffer, offset); tree:add(buffer(),"Text: " .. s)
end

local function decode_jfp2_weather_v1(tree, buffer, offset)
    local s = select(1, read_jfp2_string(buffer, offset))
    tree:add(buffer(offset, buffer:len() - offset), "Metar: " .. s)
end

local function decode_jfp2_status_request_v1(tree, buffer, offset)
    local flags = buffer(offset, 1):uint()
    tree:add(buffer(offset, 1), "Flags: 0x" .. string.format("%02x", flags) ..
        (testbit(flags, 0) and " (HubEnabled)" or "") ..
        (testbit(flags, 1) and " (HubListRequested)" or "")); offset = offset + 1
    tree:add(buffer(offset, 4), "Uuid: 0x" .. string.format("%08x", buffer(offset, 4):le_uint()))
end

local function decode_jfp2_status_v1(tree, buffer, offset)
    tree:add(buffer(offset, 16), "Guid: " .. buffer(offset, 16):bytes():tohex()); offset = offset + 16
    local s; s, offset = read_jfp2_string(buffer, offset); tree:add(buffer(),"AppVersion: " .. s)
    tree:add(buffer(offset, 2), "Users: " .. buffer(offset, 2):le_uint()); offset = offset + 2
    tree:add(buffer(offset, 2), "AtcCount: " .. buffer(offset, 2):le_uint()); offset = offset + 2
    s, offset = read_jfp2_string(buffer, offset); tree:add(buffer(),"AtcAirport: " .. s)
    tree:add(buffer(offset, 1), "AtcLevel: " .. buffer(offset, 1):uint()); offset = offset + 1
    tree:add(buffer(offset, 2), "Planes: " .. buffer(offset, 2):le_uint()); offset = offset + 2
    tree:add(buffer(offset, 2), "Helicopters: " .. buffer(offset, 2):le_uint()); offset = offset + 2
    tree:add(buffer(offset, 2), "Boats: " .. buffer(offset, 2):le_uint()); offset = offset + 2
    tree:add(buffer(offset, 2), "Vehicles: " .. buffer(offset, 2):le_uint()); offset = offset + 2
    local hubFlags = buffer(offset, 1):uint()
    tree:add(buffer(offset, 1), "HubFlags: 0x" .. string.format("%02x", hubFlags) ..
        (testbit(hubFlags, 0) and " (HubEnabled)" or "") ..
        (testbit(hubFlags, 1) and " (GlobalSession)" or "") ..
        (testbit(hubFlags, 2) and " (PasswordRequired)" or "")); offset = offset + 1
    s, offset = read_jfp2_string(buffer, offset); tree:add(buffer(),"Address: " .. s)
    s, offset = read_jfp2_string(buffer, offset); tree:add(buffer(),"Name: " .. s)
    s, offset = read_jfp2_string(buffer, offset); tree:add(buffer(),"About: " .. s)
    s, offset = read_jfp2_string(buffer, offset); tree:add(buffer(),"Voip: " .. s)
    s, offset = read_jfp2_string(buffer, offset); tree:add(buffer(),"NextEvent: " .. s)
    s, offset = read_jfp2_string(buffer, offset); tree:add(buffer(),"Airport: " .. s)
    tree:add(buffer(offset, 4), "ActivityCircle: " .. buffer(offset, 4):le_int())
end

local function decode_jfp2_guaranteed_done(tree, buffer, offset)
    -- always 3 bytes: a shorter one acknowledges nothing
    if buffer:len() < offset + 3 then
        tree:add(buffer(offset, buffer:len() - offset), "Short GuaranteedDone (" .. (buffer:len() - offset) .. " bytes): ignored")
        return
    end
    tree:add(buffer(offset, 2), "GuaranteedId: " .. buffer(offset, 2):le_uint())
    tree:add(buffer(offset + 2, 1), "GuaranteedIndex: " .. buffer(offset + 2, 1):uint())
end

-- Join (internal class 2, protocol.md §5.10): Flags u8, Proof 32 bytes
local function decode_jfp2_join(tree, buffer, offset)
    if buffer:len() < offset + 33 then
        tree:add(buffer(offset, buffer:len() - offset), "Short Join (" .. (buffer:len() - offset) .. " bytes): ignored")
        return
    end
    local flags = buffer(offset, 1):uint()
    tree:add(buffer(offset, 1), "Flags: 0x" .. string.format("%02x", flags) .. (testbit(flags, 0) and " (proof present)" or " (no proof)"))
    tree:add(buffer(offset + 1, 32), "Proof: " .. buffer(offset + 1, 32):bytes():tohex())
end

-- JoinResult (internal class 3): Result u8, Suid u32, ListSegments u8; schema version 2 (§5.15) appends Alias, a name of 8 bytes
local function decode_jfp2_join_result(tree, buffer, offset)
    if buffer:len() < offset + 6 then
        tree:add(buffer(offset, buffer:len() - offset), "Short JoinResult (" .. (buffer:len() - offset) .. " bytes): ignored")
        return
    end
    local result = buffer(offset, 1):uint()
    tree:add(buffer(offset, 1), "Result: " .. result .. " (" .. (JFP2_JOIN_RESULT[result] or "refused") .. ")")
    tree:add(buffer(offset + 1, 4), "Suid: " .. buffer(offset + 1, 4):le_uint())
    tree:add(buffer(offset + 5, 1), "ListSegments: " .. buffer(offset + 5, 1):uint())
    if buffer:len() >= offset + 14 then
        add_jfp2_name(tree, buffer, offset + 6, "Alias (version 2)")
    end
end

-- The offset after one member entry, or nil when the bytes end inside it. Schema version 2 (§5.14) has the candidates after the endpoint.
local function skip_jfp2_member_entry(buffer, offset, v2)
    local len = buffer:len()
    if offset >= len then return nil end
    offset = offset + 1 + buffer(offset, 1):uint() * 8
    if offset >= len then return nil end
    offset = offset + 1 + buffer(offset, 1):uint()
    if offset > len then return nil end
    if v2 then
        if offset >= len then return nil end
        local count = buffer(offset, 1):uint()
        offset = offset + 1
        for _ = 1, count do
            if offset >= len then return nil end
            offset = offset + 1 + buffer(offset, 1):uint()
            if offset > len then return nil end
        end
    end
    return offset
end

-- The schema version is agreed per session and not on the wire: entries that end exactly at the end of the datagram are version 1,
-- else version 2 is taken (the dissector sees a datagram, not the session).
local function jfp2_member_entries_are_v2(buffer, offset, count)
    for _ = 1, count do
        offset = skip_jfp2_member_entry(buffer, offset, false)
        if not offset then return true end
    end
    return offset ~= buffer:len()
end

-- One member entry (§5.10, §5.14): NameCount u8, names (8 bytes each), EndPointLength u8, WireEndPoint, and in version 2 CandidateCount u8
-- and that many candidates (a length byte and a WireEndPoint each).
-- Returns the offset after it, or nil when the bytes end inside it.
local function add_jfp2_member_entry(tree, buffer, offset, label, v2)
    local len = buffer:len()
    if offset >= len then return nil end
    local names = buffer(offset, 1):uint()
    if offset + 1 + names * 8 + 1 > len then return nil end
    local entry = tree:add(buffer(offset, 1), label .. ": " .. names .. " name(s)")
    offset = offset + 1
    for n = 1, names do
        add_jfp2_name(entry, buffer, offset, "Name " .. n)
        offset = offset + 8
    end
    local epLen = buffer(offset, 1):uint()
    offset = offset + 1
    if offset + epLen > len then return nil end
    local text = jfp2_wire_endpoint_string(buffer, offset, epLen)
    entry:add(buffer(offset, epLen), "EndPoint: " .. (text or "(unreadable, entry skipped)"))
    offset = offset + epLen
    if v2 then
        if offset >= len then return nil end
        local count = buffer(offset, 1):uint()
        entry:add(buffer(offset, 1), "CandidateCount: " .. count)
        offset = offset + 1
        for c = 1, count do
            if offset >= len then return nil end
            local l = buffer(offset, 1):uint()
            if offset + 1 + l > len then return nil end
            local candidate = jfp2_wire_endpoint_string(buffer, offset + 1, l)
            entry:add(buffer(offset, 1 + l), "Candidate " .. c .. ": " .. (candidate or "unassigned family or too short (skipped)"))
            offset = offset + 1 + l
        end
    end
    return offset
end

-- MemberList (internal class 4): Index u8, EntryCount u8, entries as above
local function decode_jfp2_member_list(tree, buffer, offset)
    local len = buffer:len()
    if len < offset + 2 then
        tree:add(buffer(offset, len - offset), "Short MemberList (" .. (len - offset) .. " bytes): ignored")
        return
    end
    tree:add(buffer(offset, 1), "Index: " .. buffer(offset, 1):uint())
    local count = buffer(offset + 1, 1):uint()
    tree:add(buffer(offset + 1, 1), "EntryCount: " .. count)
    offset = offset + 2
    local v2 = jfp2_member_entries_are_v2(buffer, offset, count)
    for e = 1, count do
        offset = add_jfp2_member_entry(tree, buffer, offset, "Entry " .. e, v2)
        if not offset then break end
    end
end

-- MemberAnnounce (internal class 5, §5.11): one member entry, as in MemberList
local function decode_jfp2_member_announce(tree, buffer, offset)
    if offset >= buffer:len() or not add_jfp2_member_entry(tree, buffer, offset, "Member", jfp2_member_entries_are_v2(buffer, offset, 1)) then
        tree:add(buffer(offset, buffer:len() - offset), "Short MemberAnnounce (" .. (buffer:len() - offset) .. " bytes): ignored")
    end
end

-- MemberLeave (internal class 6, §5.11): one name
local function decode_jfp2_member_leave(tree, buffer, offset)
    if buffer:len() < offset + 8 then
        tree:add(buffer(offset, buffer:len() - offset), "Short MemberLeave (" .. (buffer:len() - offset) .. " bytes): ignored")
        return
    end
    add_jfp2_name(tree, buffer, offset, "Name")
end

-- RouteQuery (internal class 7) and RouteAnswer (class 8), §5.13: Count u8 (at most 100), then that many names
local function decode_jfp2_route_names(tree, buffer, offset, label)
    local len = buffer:len()
    if len < offset + 1 then
        tree:add(buffer(offset, len - offset), "Short " .. label .. " (" .. (len - offset) .. " bytes): ignored")
        return
    end
    local count = buffer(offset, 1):uint()
    tree:add(buffer(offset, 1), "Count: " .. count)
    if count > 100 or len < offset + 1 + count * 8 then
        tree:add(buffer(offset, len - offset), label .. " not readable (count " .. count .. "): ignored")
        return
    end
    offset = offset + 1
    for n = 1, count do
        add_jfp2_name(tree, buffer, offset, "Name " .. n)
        offset = offset + 8
    end
end

-- The candidates of a directory entry (§5.16): CandidateCount u8, then each a length u8 and a WireEndPoint.
-- Returns the offset after them, or nil when they run past the datagram.
local function add_jfp2_directory_candidates(tree, buffer, offset)
    local len = buffer:len()
    if offset >= len then return nil end
    local count = buffer(offset, 1):uint()
    tree:add(buffer(offset, 1), "CandidateCount: " .. count)
    offset = offset + 1
    for c = 1, count do
        if offset >= len then return nil end
        local l = buffer(offset, 1):uint()
        if offset + 1 + l > len then return nil end
        local candidate = jfp2_wire_endpoint_string(buffer, offset + 1, l)
        tree:add(buffer(offset, 1 + l), "Candidate " .. c .. ": " .. (candidate or "unassigned family or too short (skipped)"))
        offset = offset + 1 + l
    end
    return offset
end

-- HubList (application class 10): Count u8, then per hub Name (8), Port u16, the candidates
local function decode_jfp2_hub_list(tree, buffer, offset)
    local len = buffer:len()
    if offset >= len then return end
    local count = buffer(offset, 1):uint()
    tree:add(buffer(offset, 1), "Count: " .. count)
    offset = offset + 1
    for n = 1, count do
        if offset + 11 > len then return end
        local entry = tree:add(buffer(offset, 11), "Hub " .. n)
        add_jfp2_name(entry, buffer, offset, "Name")
        entry:add(buffer(offset + 8, 2), "Port: " .. buffer(offset + 8, 2):le_uint())
        offset = add_jfp2_directory_candidates(entry, buffer, offset + 10)
        if offset == nil then return end
    end
end

-- HubUser (class 12): Guid, Flags, Nickname, Frequency, Latitude, Longitude, Altitude, Speed, Squawk, Level, Range, Heading, 13 strings
local function decode_jfp2_hub_user(tree, buffer, offset)
    if buffer:len() < offset + 17 then return end
    tree:add(buffer(offset, 16), "Guid: " .. buffer(offset, 16):bytes():tohex()); offset = offset + 16
    local flags = buffer(offset, 1):uint()
    tree:add(buffer(offset, 1), "Flags: 0x" .. string.format("%02x", flags) ..
        (testbit(flags, 0) and " (Atc)" or "") .. (testbit(flags, 1) and " (Ifr)" or "")); offset = offset + 1
    local s; s, offset = read_jfp2_string(buffer, offset); tree:add(buffer(), "Nickname: " .. s)
    if buffer:len() < offset + 18 then return end
    tree:add(buffer(offset, 2), "Frequency: " .. buffer(offset, 2):le_uint()); offset = offset + 2
    tree:add(buffer(offset, 4), "Latitude: " .. buffer(offset, 4):le_float()); offset = offset + 4
    tree:add(buffer(offset, 4), "Longitude: " .. buffer(offset, 4):le_float()); offset = offset + 4
    tree:add(buffer(offset, 2), "Altitude: " .. buffer(offset, 2):le_uint()); offset = offset + 2
    tree:add(buffer(offset, 2), "Speed: " .. buffer(offset, 2):le_uint()); offset = offset + 2
    tree:add(buffer(offset, 2), "Squawk: " .. buffer(offset, 2):le_uint()); offset = offset + 2
    tree:add(buffer(offset, 1), "Level: " .. buffer(offset, 1):uint()); offset = offset + 1
    tree:add(buffer(offset, 1), "Range: " .. buffer(offset, 1):uint()); offset = offset + 1
    tree:add(buffer(offset, 2), "Heading: " .. buffer(offset, 2):le_uint()); offset = offset + 2
    local labels = { "Callsign", "IcaoType", "Departure", "Destination", "Rules", "Route", "Remarks",
        "Alternate", "FlightSpeed", "FlightAltitude", "Registration", "IcaoAirline", "FlightNumber" }
    for _, label in ipairs(labels) do
        s, offset = read_jfp2_string(buffer, offset)
        tree:add(buffer(), label .. ": " .. s)
    end
end

-- UserPositions (class 14): Count u8, then per user 32 bytes
local function decode_jfp2_user_positions(tree, buffer, offset)
    local len = buffer:len()
    if offset >= len then return end
    local count = buffer(offset, 1):uint()
    tree:add(buffer(offset, 1), "Count: " .. count)
    offset = offset + 1
    for n = 1, count do
        if offset + 32 > len then return end
        local user = tree:add(buffer(offset, 32), "User " .. n)
        user:add(buffer(offset, 16), "Guid: " .. buffer(offset, 16):bytes():tohex())
        user:add(buffer(offset + 16, 4), "Latitude: " .. buffer(offset + 16, 4):le_float())
        user:add(buffer(offset + 20, 4), "Longitude: " .. buffer(offset + 20, 4):le_float())
        user:add(buffer(offset + 24, 2), "Altitude: " .. buffer(offset + 24, 2):le_uint())
        user:add(buffer(offset + 26, 2), "Speed: " .. buffer(offset + 26, 2):le_uint())
        user:add(buffer(offset + 28, 2), "Squawk: " .. buffer(offset + 28, 2):le_uint())
        user:add(buffer(offset + 30, 2), "Heading: " .. buffer(offset + 30, 2):le_uint())
        offset = offset + 32
    end
end

-- Online (class 15) and UserLookup (class 16): Uuid u32
local function decode_jfp2_uuid(tree, buffer, offset)
    if buffer:len() < offset + 4 then return end
    tree:add(buffer(offset, 4), "Uuid: " .. buffer(offset, 4):le_uint())
end

-- UserLookupAnswer (class 17): Uuid u32, Name (8), Port u16, the candidates
local function decode_jfp2_user_lookup_answer(tree, buffer, offset)
    if buffer:len() < offset + 15 then return end
    tree:add(buffer(offset, 4), "Uuid: " .. buffer(offset, 4):le_uint()); offset = offset + 4
    add_jfp2_name(tree, buffer, offset, "Name"); offset = offset + 8
    tree:add(buffer(offset, 2), "Port: " .. buffer(offset, 2):le_uint()); offset = offset + 2
    add_jfp2_directory_candidates(tree, buffer, offset)
end

local function decode_jfp2_application(tree, buffer, offset, class)
    local name = JFP2_APP_CLASS[class]
    local len = buffer:len()
    if name == "Position" then
        decode_jfp2_position_v1(tree, buffer, offset)
    elseif name == "Identity" then
        decode_jfp2_identity_v1(tree, buffer, offset)
    elseif name == "VariableSync" then
        decode_jfp2_variable_sync_v1(tree, buffer, offset)
    elseif name == "Event" then
        decode_jfp2_event_v1(tree, buffer, offset)
    elseif name == "FlightPlan" then
        decode_jfp2_flightplan_v1(tree, buffer, offset)
    elseif name == "Notes" then
        decode_jfp2_notes_v1(tree, buffer, offset)
    elseif name == "Weather" or name == "WeatherReply" then
        decode_jfp2_weather_v1(tree, buffer, offset)
    elseif name == "Status" then
        decode_jfp2_status_v1(tree, buffer, offset)
    elseif name == "StatusRequest" then
        decode_jfp2_status_request_v1(tree, buffer, offset)
    elseif name == "HubList" then
        decode_jfp2_hub_list(tree, buffer, offset)
    elseif name == "HubUser" then
        decode_jfp2_hub_user(tree, buffer, offset)
    elseif name == "UserPositions" then
        decode_jfp2_user_positions(tree, buffer, offset)
    elseif name == "Online" or name == "UserLookup" then
        decode_jfp2_uuid(tree, buffer, offset)
    elseif name == "UserLookupAnswer" then
        decode_jfp2_user_lookup_answer(tree, buffer, offset)
    elseif name == "UserListRequest" or name == "UserPositionsRequest" then
        if offset < len then tree:add(buffer(offset, 1), "Reserved: " .. buffer(offset, 1):uint()) end
    elseif offset < len then
        tree:add(buffer(offset, len - offset), (name or ("Unknown(" .. class .. ")")) ..
            " payload (undecoded, " .. (len - offset) .. " bytes)")
    end
end

local function decode_jfp2_internal(tree, buffer, offset, class)
    local name = JFP2_INTERNAL_CLASS[class]
    local len = buffer:len()
    if name == "Hello" then
        decode_jfp2_handshake(tree, buffer, offset, false)
    elseif name == "HelloAck" then
        decode_jfp2_handshake(tree, buffer, offset, true)
    elseif name == "Join" then
        decode_jfp2_join(tree, buffer, offset)
    elseif name == "JoinResult" then
        decode_jfp2_join_result(tree, buffer, offset)
    elseif name == "MemberList" then
        decode_jfp2_member_list(tree, buffer, offset)
    elseif name == "MemberAnnounce" then
        decode_jfp2_member_announce(tree, buffer, offset)
    elseif name == "MemberLeave" then
        decode_jfp2_member_leave(tree, buffer, offset)
    elseif name == "RouteQuery" or name == "RouteAnswer" then
        decode_jfp2_route_names(tree, buffer, offset, name)
    elseif name == "GuaranteedDone" then
        decode_jfp2_guaranteed_done(tree, buffer, offset)
    elseif offset < len then
        tree:add(buffer(offset, len - offset), (name or ("Unknown(" .. class .. ")")) ..
            " payload (undecoded, " .. (len - offset) .. " bytes)")
    end
end

----------------------------------------------------------------------
-- JFP2 dissector: 8-byte fixed envelope (+4-byte guaranteed extension,
-- +16-byte Forwarded extension), docs/network/jfp2/protocol.md §3.
----------------------------------------------------------------------

local function dissect_jfp2(buffer, pinfo, tree)
    pinfo.cols.protocol = "JFP2"
    local subtree = tree:add(joinfs_proto, buffer(), "JFP2 (JoinFS Protocol v2)")

    subtree:add(buffer(0, 1), "Magic: 0x" .. string.format("%02x", buffer(0, 1):uint()))
    subtree:add(buffer(1, 1), "ProtoMajor: " .. buffer(1, 1):uint())
    local flags = buffer(2, 1):uint()
    local isGuaranteed = testbit(flags, 0)
    local isForwarded = testbit(flags, 1)
    local isCoalesced = testbit(flags, 2)
    local isInternal = testbit(flags, 3)
    subtree:add(buffer(2, 1), "Flags: 0x" .. string.format("%02x", flags) ..
        (isGuaranteed and " [Guaranteed]" or "") ..
        (isForwarded and " [Forwarded]" or "") ..
        (isCoalesced and " [Coalesced]" or "") ..
        (isInternal and " [Internal]" or " [Application]"))
    subtree:add(buffer(3, 2), "SenderPeerId: " .. buffer(3, 2):le_uint())
    subtree:add(buffer(5, 2), "RecipientPeerId: " .. buffer(5, 2):le_uint() ..
        (buffer(5, 2):le_uint() == 0 and " (not known yet: first Hello)" or ""))

    -- NOTE: the design doc's §4.6 "Extended" escape (RawMessageClass==255 ->
    -- 2-byte real class id follows) is not actually implemented by
    -- JoinFS.Jfp2.Envelope.ReadFrom/WriteTo as of this writing - every shipped
    -- codec uses a class value well under 255. This branch is speculative/
    -- forward-compatible and should never trigger against current builds.
    local rawClass = buffer(7, 1):uint()
    local offset = 8
    local class = rawClass
    if rawClass == 255 then
        class = buffer(offset, 2):le_uint()
        subtree:add(buffer(7, 3), "RawMessageClass: 255 (Extended), real class = " .. class)
        offset = offset + 2
    else
        local className = isInternal and (JFP2_INTERNAL_CLASS[class] or class) or (JFP2_APP_CLASS[class] or class)
        subtree:add(buffer(7, 1), "RawMessageClass: " .. rawClass .. " (" .. className .. ")")
    end

    if isGuaranteed then
        subtree:add(buffer(offset, 2), "GuaranteedId: " .. buffer(offset, 2):le_uint())
        subtree:add(buffer(offset + 2, 1), "GuaranteedIndex: " .. buffer(offset + 2, 1):uint())
        subtree:add(buffer(offset + 3, 1), "GuaranteedCount: " .. buffer(offset + 3, 1):uint())
        offset = offset + 4
    end

    if isForwarded then
        offset = add_jfp2_name(subtree, buffer, offset, "Origin")
        offset = add_jfp2_name(subtree, buffer, offset, "Target")
    end

    local partitionNames = isInternal and JFP2_INTERNAL_CLASS or JFP2_APP_CLASS
    local className = partitionNames[class] or ("Unknown(" .. class .. ")")
    pinfo.cols.info = "JFP2 " .. (isInternal and "Internal" or "App") .. ": " .. className ..
        (isForwarded and " [fwd]" or "") .. (isGuaranteed and " [guaranteed]" or "")

    if isCoalesced then
        local len = buffer:len()
        local n = 0
        while offset + 3 <= len do
            n = n + 1
            local subClass = buffer(offset, 1):uint()
            local subLen = buffer(offset + 1, 2):le_uint()
            if offset + 3 + subLen > len then break end
            local subName = isInternal and (JFP2_INTERNAL_CLASS[subClass] or subClass)
                or (JFP2_APP_CLASS[subClass] or subClass)
            local subtree2 = subtree:add(buffer(offset, 3 + subLen),
                "Coalesced sub-message " .. n .. ": " .. subName .. " (" .. subLen .. " bytes)")
            local ok, err
            if isInternal then
                ok, err = pcall(decode_jfp2_internal, subtree2, buffer, offset + 3, subClass)
            else
                ok, err = pcall(decode_jfp2_application, subtree2, buffer, offset + 3, subClass)
            end
            if not ok then subtree2:add_expert_info(PI_MALFORMED, PI_ERROR, "joinfs.lua decode error: " .. tostring(err)) end
            offset = offset + 3 + subLen
        end
        return
    end

    local msgtree = subtree:add(buffer(offset, buffer:len() - offset), "Payload: " .. className)
    local ok, err
    if isInternal then
        ok, err = pcall(decode_jfp2_internal, msgtree, buffer, offset, class)
    else
        ok, err = pcall(decode_jfp2_application, msgtree, buffer, offset, class)
    end
    if not ok then msgtree:add_expert_info(PI_MALFORMED, PI_ERROR, "joinfs.lua decode error: " .. tostring(err)) end
end

----------------------------------------------------------------------
-- Top-level dissect: one magic-byte compare routes to the right decoder,
-- exactly like LocalNode.ReceiveMessages does on the real socket
-- (docs/network/jfp2/protocol.md §2.2).
----------------------------------------------------------------------

function joinfs_proto.dissector(buffer, pinfo, tree)
    local len = buffer:len()
    if len < 1 then return 0 end

    local b0 = buffer(0, 1):uint()
    if b0 == 0xfa then
        if len < 8 then return 0 end
        dissect_jfp2(buffer, pinfo, tree)
        return len
    elseif len >= 2 and buffer(0, 2):le_uint() == 0x520b then
        if len < 21 then return 0 end
        dissect_legacy(buffer, pinfo, tree)
        return len
    end
    return 0
end

-- Heuristic dissector so this fires even on non-default ports (P2P peers
-- and hubs can be configured on any UDP port).
local function joinfs_heuristic(buffer, pinfo, tree)
    if buffer:len() < 1 then return false end
    local b0 = buffer(0, 1):uint()
    if b0 == 0xfa and buffer:len() >= 8 then
        joinfs_proto.dissector(buffer, pinfo, tree)
        return true
    elseif buffer:len() >= 21 and buffer(0, 2):le_uint() == 0x520b then
        joinfs_proto.dissector(buffer, pinfo, tree)
        return true
    end
    return false
end

joinfs_proto:register_heuristic("udp", joinfs_heuristic)

local udp_port_table = DissectorTable.get("udp.port")
udp_port_table:add(6112, joinfs_proto) -- Network.DEFAULT_PORT

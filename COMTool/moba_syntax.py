"""MobaXterm-compatible keyword highlighting for plain terminal output.

This mirrors MobaXterm's default "Custom: OK/warning/error keywords" syntax
profile.  MobaXterm highlights only the matched keywords, not whole lines, so
plain command output stays readable while status words and addresses get the
usual terminal colors.

The profile's leading/trailing boundary classes are implemented as
lookarounds.  That keeps the keyword lists identical to MobaXterm while also
handling tokens at the start/end of a line and adjacent tokens.
"""

import re


_BOUNDARY_BEFORE = r"(?<![A-Za-z_&-])"
_BOUNDARY_AFTER = r"(?![A-Za-z_-])"
_IP_BOUNDARY_BEFORE = r"(?<![0-9A-Za-z_&-])"
_IP_BOUNDARY_AFTER = r"(?![0-9A-Za-z_-])"


_URL = re.compile(
    _BOUNDARY_BEFORE
    + r"(https?://[A-Za-z0-9_.&?=%~#{}()@+\-]+:?[A-Za-z0-9_./&?=%~#{}()@+\-]+)"
    + r"(?![A-Za-z0-9_-])",
    re.IGNORECASE,
)

_ERROR = re.compile(
    _BOUNDARY_BEFORE
    + r"(?:"
    r"(?:bad|wrong|incorrect|improper|invalid|unsupported)(?: file| memory)? "
    r"(?:descriptor|alloc(?:ation)?|addr(?:ess)?|owner(?:ship)?|arg(?:ument)?|"
    r"param(?:eter)?|setting|length|filename)"
    r"|not properly"
    r"|improperly"
    r"|(?:operation |connection |authentication |access |permission )?"
    r"(?:denied|disallowed|not allowed|refused|problem|failed|failure|not permitted)"
    r"|no [A-Za-z]+(?: [A-Za-z]+)? found"
    r"|invalid"
    r"|unsupported"
    r"|not supported"
    r"|seg(?:mentation )?fault"
    r"|corruption"
    r"|corrupted"
    r"|corrupt"
    r"|overflow"
    r"|underrun"
    r"|not ok"
    r"|unimplemented"
    r"|unsuccessfull"
    r"|not implemented"
    r"|permerrors?"
    r"|fehlers?"
    r"|errore"
    r"|errors?"
    r"|erreurs?"
    r"|fejl"
    r"|virhe"
    r"|greška"
    r"|erro"
    r"|fel"
    r"|\(ee\)"
    r"|\(ni\)"
    r")"
    + _BOUNDARY_AFTER,
    re.IGNORECASE,
)

_SUCCESS = re.compile(
    _BOUNDARY_BEFORE
    + r"(?:accepted|allowed|enabled|connected|erfolgreich|exitoso|successo|"
    r"sucedido|framgångsrik|successfully|successful|succeeded|success)"
    + _BOUNDARY_AFTER,
    re.IGNORECASE,
)

_WARNING = re.compile(
    _BOUNDARY_BEFORE
    + r"(?:"
    r"\[\-w[A-Za-z-]+\]"
    r"|caught signal [0-9]+"
    r"|cannot"
    r"|(?:connection (?:to (?:remote host|[a-z0-9.]+) )?)?"
    r"(?:closed|terminated|stopped|not responding)"
    r"|exited"
    r"|no more [A-Za-z] available"
    r"|unexpected"
    r"|(?:command |binary |file )?not found"
    r"|ooo?o?o?ps"
    r"|out of (?:space|memory)"
    r"|low (?:memory|disk)"
    r"|unknown"
    r"|disabled"
    r"|disconnected"
    r"|deprecated"
    r"|refused"
    r"|disconnect(?:ion)?"
    r"|advertencia"
    r"|avvertimento"
    r"|attention"
    r"|warnings?"
    r"|achtung"
    r"|exclamation"
    r"|alerts?"
    r"|warnungs?"
    r"|advarsel"
    r"|pedwarn"
    r"|aviso"
    r"|varoitus"
    r"|upozorenje"
    r"|peringatan"
    r"|uyari"
    r"|varning"
    r"|avertissement"
    r"|\(ww\)"
    r"|\(\?\?\)"
    r"|could not"
    r"|unable to"
    r")"
    + _BOUNDARY_AFTER,
    re.IGNORECASE,
)

_MAGENTA = re.compile(
    _IP_BOUNDARY_BEFORE
    + r"(?:localhost|"
    r"(?:[1-9]|[1-9][0-9]|1[0-9][0-9]|2[0-4][0-9]|25[0-4])"
    r"\.[0-9]+\.[0-9]+\.[0-9]+|null|none)"
    + _IP_BOUNDARY_AFTER,
    re.IGNORECASE,
)

_CYAN = re.compile(
    _BOUNDARY_BEFORE
    + r"(?:last (?:failed )?login:|launching|checking|loading|creating|"
    r"building|important|booting|starting|notice|informational|informationen|"
    r"informazioni|informação|oplysninger|informations?|info|información|"
    r"informasi|note|\(ii\)|\(\!\!\))"
    + _BOUNDARY_AFTER,
    re.IGNORECASE,
)

# Boolean literals are only highlighted in the contexts MobaXterm uses, e.g.
# `status=false` or `(true)`.  The surrounding punctuation stays uncolored.
_BOOLEAN_ERROR = re.compile(
    r"""[=>"':.,;({\[][ ]*(false|no|ko)(?=[ ]*[]=>"':.,;)} ]|$)""",
    re.IGNORECASE,
)
_BOOLEAN_SUCCESS = re.compile(
    r"""[=>"':.,;({\[][ ]*(true|yes|ok)(?=[ ]*[]=>"':.,;)} ]|$)""",
    re.IGNORECASE,
)

# Later rules do not overwrite cells already colored by an earlier rule, just
# like MobaXterm applies its profile entries in order.
_RULES = (
    (_URL, "underline", 1),
    (_ERROR, "brightred", 0),
    (_SUCCESS, "brightgreen", 0),
    (_WARNING, "brightyellow", 0),
    (_MAGENTA, "brightmagenta", 0),
    (_CYAN, "brightcyan", 0),
    (_BOOLEAN_ERROR, "brightred", 1),
    (_BOOLEAN_SUCCESS, "brightgreen", 1),
)


def highlight_line(text):
    """Return a color name for every character in *text* (or ``None``)."""
    colors = [None] * len(text)
    for pattern, color, group in _RULES:
        for match in pattern.finditer(text):
            start = match.start(group)
            end = match.end(group)
            for index in range(start, end):
                if colors[index] is None:
                    colors[index] = color
    return colors
